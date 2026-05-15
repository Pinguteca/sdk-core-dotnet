using System;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Pinguteca.Sdk.Core.Idempotency;

/// <summary>
/// gRPC client interceptor that stamps an idempotency key header on
/// every unary call so the server can deduplicate replays.
///
/// Cross-SDK contract pinned in
/// sdk-scaffold/docs/rfc/0008-resilience-presets.md: idempotency sits
/// above retry in the chain so the key is generated once on the
/// first attempt and the same header replays on every retry.
///
/// Keys are UUIDv7 (time-ordered, CSPRNG-backed per RFC 0007). If the
/// caller already supplied the header, the interceptor leaves it
/// untouched.
/// </summary>
public sealed class IdempotencyInterceptor : Interceptor
{
    private readonly IdempotencyOptions _options;
    private readonly Func<string> _keyFactory;

    public IdempotencyInterceptor(IdempotencyOptions? options = null)
    {
        _options = options ?? new IdempotencyOptions();
        _keyFactory = _options.KeyFactory ?? DefaultKey;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var newContext = ApplyKey(context);
        return continuation(request, newContext);
    }

    private ClientInterceptorContext<TRequest, TResponse> ApplyKey<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var headers = CloneHeaders(context.Options.Headers);
        if (FindHeader(headers, _options.HeaderName) is not null)
        {
            return context;
        }
        headers.Add(_options.HeaderName, _keyFactory());
        var newOptions = context.Options.WithHeaders(headers);
        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            newOptions);
    }

    private static Metadata CloneHeaders(Metadata? source)
    {
        var clone = new Metadata();
        if (source is null)
        {
            return clone;
        }
        foreach (var entry in source)
        {
            clone.Add(entry);
        }
        return clone;
    }

    private static Metadata.Entry? FindHeader(Metadata headers, string name)
    {
        foreach (var entry in headers)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }

    private static string DefaultKey() => Guid.CreateVersion7().ToString();
}
