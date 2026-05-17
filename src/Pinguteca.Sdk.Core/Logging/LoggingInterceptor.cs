using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Pinguteca.Sdk.Core.Logging;

/// <summary>
/// gRPC client interceptor that emits one structured record per RPC
/// at completion (canonical-log + wide-event pattern). No per-step
/// debug noise.
///
/// Cross-SDK contract pinned in
/// <c>sdk-scaffold/docs/rfc/0010-structured-logging.md</c>: the
/// canonical record carries <c>rpc.system</c>, <c>rpc.service</c>,
/// <c>rpc.method</c>, <c>rpc.duration_ms</c>, <c>rpc.code</c>,
/// optional <c>request.id</c>, optional <c>trace.id</c> and
/// <c>span.id</c> from <see cref="Activity.Current"/>, optional
/// <c>error</c> on failure, plus whatever the caller-supplied hooks
/// return.
///
/// Position in the chain: outermost in the observability layer,
/// wrapping <see cref="Otel.OtelInterceptor"/> so the recorded
/// <c>rpc.duration_ms</c> equals the OTel span's total duration.
/// Streaming RPCs pass through unchanged.
/// </summary>
public sealed class LoggingInterceptor : Interceptor
{
    private readonly LoggingOptions _options;
    private readonly HashSet<string> _redact;

    public LoggingInterceptor(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Logger is null)
        {
            throw new ArgumentException("Logger is required", nameof(options));
        }
        _options = options;
        _redact = new HashSet<string>(options.RedactHeaders, StringComparer.OrdinalIgnoreCase);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var started = Stopwatch.GetTimestamp();
        var headers = context.Options.Headers;
        var requestCtx = new LoggingRequestContext(
            context.Method.ServiceName,
            context.Method.Name,
            headers);

        var call = continuation(request, context);

        return new AsyncUnaryCall<TResponse>(
            WrapResponseAsync(call.ResponseAsync, started, context, requestCtx),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private async Task<TResponse> WrapResponseAsync<TRequest, TResponse>(
        Task<TResponse> inner,
        long started,
        ClientInterceptorContext<TRequest, TResponse> context,
        LoggingRequestContext requestCtx)
        where TRequest : class
        where TResponse : class
    {
        TResponse? response = default;
        Exception? error = null;
        try
        {
            response = await inner.ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var code = StatusFromError(error);
            Emit(elapsed, code, error, context, requestCtx);
        }
    }

    private void Emit<TRequest, TResponse>(
        TimeSpan elapsed,
        StatusCode code,
        Exception? error,
        ClientInterceptorContext<TRequest, TResponse> context,
        LoggingRequestContext requestCtx)
        where TRequest : class
        where TResponse : class
    {
        var attrs = new List<KeyValuePair<string, object?>>(capacity: 12)
        {
            new("rpc.system", "grpc"),
            new("rpc.service", context.Method.ServiceName),
            new("rpc.method", context.Method.Name),
            new("rpc.duration_ms", (long)elapsed.TotalMilliseconds),
            new("rpc.code", error is null ? "OK" : code.ToString()),
        };

        var headers = context.Options.Headers;
        if (headers is not null)
        {
            var requestId = headers.GetValue(_options.RequestIdHeader);
            if (!string.IsNullOrEmpty(requestId))
            {
                attrs.Add(new("request.id", requestId));
            }
        }

        var activity = Activity.Current;
        if (activity is not null && activity.TraceId != default)
        {
            attrs.Add(new("trace.id", activity.TraceId.ToString()));
            attrs.Add(new("span.id", activity.SpanId.ToString()));
        }

        if (_options.AddRequestAttrs is not null)
        {
            attrs.AddRange(_options.AddRequestAttrs(requestCtx));
        }

        if (_options.AddResponseAttrs is not null)
        {
            var responseCtx = new LoggingResponseContext(
                context.Method.ServiceName,
                context.Method.Name,
                code,
                error);
            attrs.AddRange(_options.AddResponseAttrs(responseCtx));
        }

        if (error is not null)
        {
            attrs.Add(new("error", error.Message));
        }

        if (_options.LogHeaders && headers is not null)
        {
            attrs.Add(new("rpc.headers", RedactedHeaders(headers)));
        }

        var level = error is null ? _options.SuccessLevel : _options.ErrorLevel;
        _options.Logger.Log(
            level,
            eventId: default,
            state: attrs,
            exception: null,
            formatter: static (_, _) => string.Empty);
    }

    private IReadOnlyDictionary<string, string> RedactedHeaders(Metadata headers)
    {
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in headers)
        {
            if (entry.IsBinary)
            {
                continue;
            }
            result[entry.Key] = _redact.Contains(entry.Key) ? "[REDACTED]" : entry.Value;
        }
        return result;
    }

    private static StatusCode StatusFromError(Exception? error) => error switch
    {
        null => StatusCode.OK,
        RpcException rpc => rpc.StatusCode,
        OperationCanceledException => StatusCode.Cancelled,
        _ => StatusCode.Unknown,
    };
}
