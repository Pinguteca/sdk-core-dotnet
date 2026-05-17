using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Errors;

namespace Pinguteca.Sdk.Core.Auth;

/// <summary>
/// gRPC client interceptor that, on a single
/// <c>Unauthenticated</c> response, calls
/// <see cref="IRotatingTokenSource.Invalidate"/> and retries the
/// call exactly once. The retry re-enters the inner
/// <see cref="AuthInterceptor"/>, which sees the cleared cache and
/// fetches a fresh token.
///
/// Cross-SDK contract pinned in
/// <c>sdk-scaffold/docs/rfc/0012-token-rotation-policy.md</c>:
/// place this interceptor INSIDE retry (so a transient
/// post-rotation network failure still benefits from retry's
/// backoff) and OUTSIDE auth (so the retry-after-invalidate attempt
/// re-runs the inner auth interceptor and pulls a fresh token).
///
/// Canonical chain order:
/// <code>
/// Logging -&gt; OTel -&gt; Breaker -&gt; Idempotency -&gt; Retry -&gt; Rotation -&gt; Auth
/// </code>
///
/// One-shot retry, never a loop. A persistent
/// <c>Unauthenticated</c> after rotation indicates bad credentials
/// or misconfiguration, not credential expiry, and looping would
/// mask misconfiguration and amplify load on the IdP.
///
/// Idempotency safety gate enabled by default
/// (<see cref="RotationOptions.AllowNonIdempotent"/> = false): the
/// original RPC may have been processed server-side before the 401
/// came back, and retrying a non-idempotent mutation could create
/// a duplicate write. Streaming RPCs pass through unchanged because
/// streams cannot be replayed safely.
/// </summary>
public sealed class RotationInterceptor : Interceptor
{
    private readonly RotationOptions _options;

    public RotationInterceptor(RotationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Source is null)
        {
            throw new ArgumentException("Source is required.", nameof(options));
        }
        _options = options;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var response = HandleAsync(request, context, continuation);
        return new AsyncUnaryCall<TResponse>(
            response,
            response.ContinueWith(_ => new Metadata(), TaskScheduler.Default),
            () => response.IsCompletedSuccessfully ? Status.DefaultSuccess : Status.DefaultCancelled,
            () => [],
            () => { });
    }

    private async Task<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        if (!ShouldRotate(context))
        {
            return await continuation(request, context).ResponseAsync.ConfigureAwait(false);
        }

        try
        {
            return await continuation(request, context).ResponseAsync.ConfigureAwait(false);
        }
        catch (SdkError ex) when (ex.Code == SdkErrorCode.Unauthenticated)
        {
            _options.Source.Invalidate();
            return await continuation(request, context).ResponseAsync.ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
        {
            _options.Source.Invalidate();
            return await continuation(request, context).ResponseAsync.ConfigureAwait(false);
        }
    }

    private bool ShouldRotate<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (_options.AllowNonIdempotent)
        {
            return true;
        }
        if (_options.IsIdempotent is null)
        {
            // Schema-blind .NET with no hook: per RFC 0006, default to
            // open-gate behaviour. Consumers who want the safety gate
            // wire the hook (or set AllowNonIdempotent = false with the
            // hook returning false for unsafe methods).
            return true;
        }
        var procedure = $"/{context.Method.ServiceName}/{context.Method.Name}";
        return _options.IsIdempotent(procedure);
    }
}
