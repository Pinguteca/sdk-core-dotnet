using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Pinguteca.Sdk.Core.Caching;

/// <summary>
/// gRPC client interceptor that caches unary responses per RFC
/// 0015. Streams pass through unchanged.
///
/// Caching is interceptor-layer (not HTTP-layer) because
/// Grpc.Net.Client speaks gRPC over HTTP/2 POST and has no native
/// HTTP cache semantics for unary calls. Cache hits short-circuit
/// the rest of the chain (no retry, breaker, idempotency, or auth
/// runs).
///
/// Default-deny tenant isolation: when
/// <see cref="CachingOptions.Store"/> or
/// <see cref="CachingOptions.KeyScope"/> is null the interceptor
/// passes every call through without caching. Multi-tenant
/// deployments must wire <see cref="CachingOptions.KeyScope"/>.
/// </summary>
public sealed class CachingInterceptor : Interceptor
{
    private readonly CachingOptions _options;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _singleFlight = new();

    public CachingInterceptor(CachingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _now = options.Now ?? (() => DateTimeOffset.UtcNow);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        if (_options.Store is null || _options.KeyScope is null)
        {
            return continuation(request, context);
        }

        var procedure = $"/{context.Method.ServiceName}/{context.Method.Name}";
        if (!_options.MethodConfig.TryGetValue(procedure, out var spec))
        {
            return continuation(request, context);
        }

        if (request is not IMessage)
        {
            // Non-protobuf custom marshallers cannot be serialised into
            // a stable cache key; pass through rather than misbehave.
            return continuation(request, context);
        }

        if (!spec.Cacheable)
        {
            return WrapWithInvalidation(request, context, continuation, spec, procedure);
        }

        return WrapWithCaching(request, context, continuation, spec, procedure);
    }

    private AsyncUnaryCall<TResponse> WrapWithCaching<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
        CacheSpec spec,
        string procedure)
        where TRequest : class
        where TResponse : class
    {
        var task = ExecuteCachedAsync(request, context, continuation, spec, procedure);
        return new AsyncUnaryCall<TResponse>(
            task,
            Task.FromResult(new Metadata()),
            () => task.IsCompletedSuccessfully ? Status.DefaultSuccess : Status.DefaultCancelled,
            () => [],
            () => { });
    }

    private AsyncUnaryCall<TResponse> WrapWithInvalidation<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
        CacheSpec spec,
        string procedure)
        where TRequest : class
        where TResponse : class
    {
        var inner = continuation(request, context);
        var task = AwaitAndInvalidateAsync(inner.ResponseAsync, context, spec, procedure);
        return new AsyncUnaryCall<TResponse>(
            task,
            inner.ResponseHeadersAsync,
            inner.GetStatus,
            inner.GetTrailers,
            inner.Dispose);
    }

    private async Task<TResponse> AwaitAndInvalidateAsync<TRequest, TResponse>(
        Task<TResponse> inner,
        ClientInterceptorContext<TRequest, TResponse> context,
        CacheSpec spec,
        string procedure)
        where TRequest : class
        where TResponse : class
    {
        var response = await inner.ConfigureAwait(false);
        var scope = _options.KeyScope!(BuildCallContext(context, procedure));
        var service = ServiceFromProcedure(procedure);
        foreach (var method in spec.Invalidates)
        {
            var prefix = string.Concat(scope, ":", service, method, ":");
            await _options.Store!.DeleteMatchingAsync(prefix, context.Options.CancellationToken)
                .ConfigureAwait(false);
        }
        return response;
    }

    private async Task<TResponse> ExecuteCachedAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
        CacheSpec spec,
        string procedure)
        where TRequest : class
        where TResponse : class
    {
        var requestBytes = CacheKey.SerializeRequest((IMessage)request);
        var scope = _options.KeyScope!(BuildCallContext(context, procedure));
        var key = CacheKey.Build(scope, procedure, requestBytes);
        var ct = context.Options.CancellationToken;

        var lookup = await _options.Store!.GetAsync(key, ct).ConfigureAwait(false);
        var now = _now();

        if (lookup is { Found: true, Entry: { } cached })
        {
            if (!cached.Expired(now))
            {
                Log(LogLevel.Information, "hit", procedure, cached, now);
                return Deserialize<TResponse>(context.Method.ResponseMarshaller, cached.Body);
            }
            if (cached.Stale(now))
            {
                Log(LogLevel.Information, "swr-hit", procedure, cached, now);
                // Background refresh outlives the foreground request: detach
                // cancellation so a request-scoped cancellation does not
                // abort the refresh that other callers depend on.
                var bgContext = new ClientInterceptorContext<TRequest, TResponse>(
                    context.Method,
                    context.Host,
                    context.Options.WithCancellationToken(CancellationToken.None));
                _ = Task.Run(
                    () => FetchAndStoreAsync(request, bgContext, continuation, spec, key, cached, procedure),
                    CancellationToken.None);
                return Deserialize<TResponse>(context.Method.ResponseMarshaller, cached.Body);
            }
        }

        Log(LogLevel.Information, "miss", procedure, null, now);
        return await FetchAndStoreAsync(request, context, continuation, spec, key, lookup.Entry, procedure)
            .ConfigureAwait(false);
    }

    private async Task<TResponse> FetchAndStoreAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
        CacheSpec spec,
        string key,
        Entry? previous,
        string procedure)
        where TRequest : class
        where TResponse : class
    {
        var ct = context.Options.CancellationToken;
        var gate = _singleFlight.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock; a concurrent caller may
            // have populated the cache while we waited.
            var rechecked = await _options.Store!.GetAsync(key, ct).ConfigureAwait(false);
            if (rechecked is { Found: true, Entry: { } cached } && !cached.Expired(_now()))
            {
                return Deserialize<TResponse>(context.Method.ResponseMarshaller, cached.Body);
            }

            var contextWithEtag = previous?.ETag is { Length: > 0 }
                ? AddIfNoneMatch(context, previous.ETag)
                : context;

            var inner = continuation(request, contextWithEtag);
            try
            {
                var response = await inner.ResponseAsync.ConfigureAwait(false);
                var entry = await StoreResponseAsync(
                    inner, response, spec, key, ct, procedure).ConfigureAwait(false);
                // Cached body matches the response we just got; return as-is.
                _ = entry;
                return response;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound && spec.NegativeTtl > TimeSpan.Zero)
            {
                var entry = new Entry
                {
                    Status = StatusCode.NotFound,
                    Body = Array.Empty<byte>(),
                    Created = _now(),
                    Ttl = spec.NegativeTtl,
                };
                await _options.Store!.SetAsync(key, entry, ct).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            gate.Release();
            // Best-effort cleanup; another caller racing on the same key
            // simply re-adds the semaphore.
            if (gate.CurrentCount == 1)
            {
                _singleFlight.TryRemove(key, out _);
            }
        }
    }

    private async Task<Entry> StoreResponseAsync<TResponse>(
        AsyncUnaryCall<TResponse> inner,
        TResponse response,
        CacheSpec spec,
        string key,
        CancellationToken ct,
        string procedure)
        where TResponse : class
    {
        var bytes = ((IMessage)response).ToByteArray();
        var trailers = inner.GetTrailers();
        var etag = ExtractEtag(trailers);
        var entry = new Entry
        {
            Body = bytes,
            Headers = CaptureHeaders(trailers),
            ETag = etag,
            Status = StatusCode.OK,
            Created = _now(),
            Ttl = spec.Ttl,
            Swr = spec.Swr,
        };
        await _options.Store!.SetAsync(key, entry, ct).ConfigureAwait(false);
        Log(LogLevel.Debug, "stored", procedure, entry, _now());
        return entry;
    }

    private static ClientInterceptorContext<TRequest, TResponse> AddIfNoneMatch<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        string etag)
        where TRequest : class
        where TResponse : class
    {
        var headers = context.Options.Headers ?? [];
        var newHeaders = new Metadata();
        foreach (var entry in headers)
        {
            newHeaders.Add(entry);
        }
        newHeaders.Add("if-none-match", etag);
        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host, context.Options.WithHeaders(newHeaders));
    }

    private static CallContext BuildCallContext<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        string procedure)
        where TRequest : class
        where TResponse : class
    {
        return new CallContext(procedure, context.Options.Headers ?? []);
    }

    private static string ServiceFromProcedure(string procedure)
    {
        var lastSlash = procedure.LastIndexOf('/');
        return lastSlash <= 0 ? procedure : procedure[..(lastSlash + 1)];
    }

    private static string? ExtractEtag(Metadata trailers)
    {
        foreach (var entry in trailers)
        {
            if (string.Equals(entry.Key, "etag", StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }
        return null;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CaptureHeaders(Metadata trailers)
    {
        var captured = new List<KeyValuePair<string, string>>(trailers.Count);
        foreach (var entry in trailers)
        {
            if (entry.IsBinary)
            {
                continue;
            }
            captured.Add(new KeyValuePair<string, string>(entry.Key, entry.Value));
        }
        return captured;
    }

    private static TResponse Deserialize<TResponse>(Marshaller<TResponse> marshaller, byte[] bytes)
        where TResponse : class
    {
        return marshaller.ContextualDeserializer(new BufferDeserializationContext(bytes));
    }

    private void Log(LogLevel level, string outcome, string procedure, Entry? entry, DateTimeOffset now)
    {
        if (_options.Logger is null) return;
        var ageMs = entry is null ? 0 : (long)(now - entry.Created).TotalMilliseconds;
        _options.Logger.Log(
            level,
            "cache outcome={Outcome} method={Method} age_ms={AgeMs}",
            outcome, procedure, ageMs);
    }

    private sealed class BufferDeserializationContext : DeserializationContext
    {
        private readonly byte[] _bytes;
        public BufferDeserializationContext(byte[] bytes) => _bytes = bytes;
        public override int PayloadLength => _bytes.Length;
        public override byte[] PayloadAsNewBuffer() => (byte[])_bytes.Clone();
        public override ReadOnlySequence<byte> PayloadAsReadOnlySequence() => new(_bytes);
    }
}
