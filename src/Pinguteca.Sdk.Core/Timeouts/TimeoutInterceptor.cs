using System;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Pinguteca.Sdk.Core.Timeouts;

/// <summary>
/// gRPC client interceptor that stamps a default deadline on every
/// unary call when the caller has not supplied one. Streaming calls
/// pass through unchanged because long-lived streams are caller-
/// driven.
///
/// The interceptor sets <see cref="CallOptions.Deadline"/> on the
/// outgoing call only when it is null. Callers who set a per-call
/// deadline retain full control.
/// </summary>
public sealed class TimeoutInterceptor : Interceptor
{
    private readonly TimeoutOptions _options;
    private readonly Func<DateTime> _utcNow;

    public TimeoutInterceptor(TimeoutOptions? options = null, Func<DateTime>? utcNow = null)
    {
        _options = options ?? new TimeoutOptions();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var newContext = ApplyDeadline(context);
        return continuation(request, newContext);
    }

    private ClientInterceptorContext<TRequest, TResponse> ApplyDeadline<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (_options.Default is not { } @default || context.Options.Deadline is not null)
        {
            return context;
        }
        var newOptions = context.Options.WithDeadline(_utcNow() + @default);
        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            newOptions);
    }
}
