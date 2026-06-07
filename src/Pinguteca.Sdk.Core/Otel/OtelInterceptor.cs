using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Pinguteca.Sdk.Core.Otel;

/// <summary>
/// gRPC client interceptor that opens a client-kind activity around
/// every unary call so the work of every interceptor under it (breaker,
/// idempotency, retry, auth) appears as descendants of one span.
///
/// Cross-SDK contract pinned in
/// sdk-scaffold/docs/rfc/0008-resilience-presets.md: OTel sits
/// outermost in the chain. The activity carries OpenTelemetry RPC
/// semantic conventions
/// (https://opentelemetry.io/docs/specs/semconv/rpc/grpc/) so
/// downstream collectors render gRPC traces without per-SDK glue.
/// </summary>
public sealed class OtelInterceptor : Interceptor
{
    private readonly ActivitySource _source;

    public OtelInterceptor(OtelOptions? options = null)
    {
        var name = (options ?? new OtelOptions()).ActivitySourceName;
        _source = new ActivitySource(name);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        // OTel RPC semantic conventions name the span as
        // "{package}.{Service}/{Method}" (no leading slash).
        // Grpc.Core's FullName carries a leading slash, so compose
        // the name from the parts instead of passing FullName.
        // Reference: https://opentelemetry.io/docs/specs/semconv/rpc/grpc/
        var activity = _source.StartActivity(
            $"{context.Method.ServiceName}/{context.Method.Name}",
            ActivityKind.Client);

        if (activity is not null)
        {
            activity.SetTag("rpc.system", "grpc");
            activity.SetTag("rpc.service", context.Method.ServiceName);
            activity.SetTag("rpc.method", context.Method.Name);
        }

        var call = continuation(request, context);

        return new AsyncUnaryCall<TResponse>(
            WrapResponseAsync(call.ResponseAsync, activity),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            () =>
            {
                activity?.Dispose();
                call.Dispose();
            });
    }

    private static async Task<TResponse> WrapResponseAsync<TResponse>(
        Task<TResponse> inner,
        Activity? activity)
    {
        try
        {
            var response = await inner.ConfigureAwait(false);
            if (activity is not null)
            {
                activity.SetTag("rpc.grpc.status_code", (int)StatusCode.OK);
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            return response;
        }
        catch (RpcException ex)
        {
            if (activity is not null)
            {
                activity.SetTag("rpc.grpc.status_code", (int)ex.StatusCode);
                activity.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            }
            throw;
        }
    }
}
