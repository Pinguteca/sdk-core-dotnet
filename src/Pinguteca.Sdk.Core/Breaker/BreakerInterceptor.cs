using System;
using System.Globalization;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Pinguteca.Sdk.Core.Breaker;

/// <summary>
/// gRPC client interceptor that short-circuits calls while the
/// breaker is open and re-tests the upstream with a single half-
/// open probe after the open window expires.
///
/// Cross-SDK contract pinned in
/// sdk-scaffold/docs/rfc/0008-resilience-presets.md: the breaker
/// sits above retry in the chain so short-circuited calls do not
/// consume retry budget or generate idempotency keys. Open-state
/// errors carry a retry-after hint that the retry interceptor
/// reads through <see cref="Errors.SdkError.RetryAfter"/>.
///
/// The hint travels as a textual <c>retry-after</c> trailer (in
/// seconds) rather than a structured <c>google.rpc.RetryInfo</c>
/// detail. The cross-SDK RFC names RetryInfo as the canonical
/// shape but the SDK does not yet parse it; revisit once the
/// status-detail dependency is justified.
/// </summary>
public sealed class BreakerInterceptor : Interceptor
{
    private readonly CircuitBreaker _breaker;

    public BreakerInterceptor(BreakerOptions? options = null)
    {
        _breaker = new CircuitBreaker(options ?? new BreakerOptions());
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var decision = _breaker.TryAcquire();
        if (!decision.Allow)
        {
            var rejection = CreateOpenStateException(decision.RetryAfter);
            return new AsyncUnaryCall<TResponse>(
                Task.FromException<TResponse>(rejection),
                Task.FromResult(new Metadata()),
                () => rejection.Status,
                () => rejection.Trailers,
                () => { });
        }

        var call = continuation(request, context);
        var observed = ObserveResponseAsync(call.ResponseAsync);
        return new AsyncUnaryCall<TResponse>(
            observed,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private async Task<TResponse> ObserveResponseAsync<TResponse>(Task<TResponse> inner)
    {
        try
        {
            var response = await inner.ConfigureAwait(false);
            _breaker.RecordSuccess();
            return response;
        }
        catch (RpcException)
        {
            _breaker.RecordFailure();
            throw;
        }
    }

    private static RpcException CreateOpenStateException(TimeSpan retryAfter)
    {
        var seconds = retryAfter.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var trailers = new Metadata { { "retry-after", seconds } };
        var status = new Status(StatusCode.Unavailable, "circuit breaker open");
        return new RpcException(status, trailers);
    }
}
