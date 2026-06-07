using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Errors;

namespace Pinguteca.Sdk.Core.Retry;

/// <summary>
/// gRPC client interceptor implementing the retry behavioural
/// contract from RFC 0006. Supports both full (default) and
/// decorrelated jitter, honours server-supplied retry hints, and
/// retries on the canonical retryable status set
/// (Unavailable, ResourceExhausted, Aborted, DeadlineExceeded).
///
/// Streaming calls pass through; replaying a stream is unsafe
/// without server cooperation that this layer does not assume.
///
/// The idempotency safety gate from RFC 0006 is not enforced in
/// this implementation: the C# gRPC ecosystem does not surface
/// the proto <c>idempotency_level</c> option at runtime. Callers
/// who need the gate should narrow <see cref="RetryOptions.IsRetryable"/>
/// or use a per-method registry until the cross-SDK plugin lands.
/// The RFC 0006 per-language table documents this divergence.
/// </summary>
public sealed class RetryInterceptor : Interceptor
{
    private readonly RetryOptions _options;
    private readonly IRetryRandom _random;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public RetryInterceptor(RetryOptions? options = null)
    {
        _options = options ?? new RetryOptions();
        if (_options.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.MaxAttempts, "MaxAttempts must be at least 1");
        }
        if (_options.Multiplier < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.Multiplier, "Multiplier must be at least 1.0");
        }
        if (_options.DecorrelationFactor < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.DecorrelationFactor, "DecorrelationFactor must be at least 1.0");
        }
        _random = _options.Random ?? CryptoRetryRandom.Instance;
        _delay = _options.Delay ?? (_options.TimeProvider is { } tp
            ? (d, ct) => Task.Delay(d, tp, ct)
            : Task.Delay);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var response = RunWithRetryAsync(request, context, continuation);
        return new AsyncUnaryCall<TResponse>(
            response,
            response.ContinueWith(_ => new Metadata(), TaskScheduler.Default),
            () => response.IsCompletedSuccessfully ? Status.DefaultSuccess : Status.DefaultCancelled,
            () => [],
            () => { });
    }

    private async Task<TResponse> RunWithRetryAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        var token = context.Options.CancellationToken;
        var ceiling = _options.BaseDelay;
        var previous = _options.BaseDelay;
        RpcException? lastError = null;

        for (var attempt = 0; attempt < _options.MaxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var call = continuation(request, context);
                return await call.ResponseAsync.ConfigureAwait(false);
            }
            catch (RpcException ex) when (_options.IsRetryable(ex.StatusCode) && attempt < _options.MaxAttempts - 1)
            {
                lastError = ex;
                var sleep = ResolveBackoff(ex, ceiling, previous);
                previous = sleep;
                ceiling = RetryPolicy.GrowCeiling(ceiling, _options.Multiplier, _options.MaxDelay);
                await _delay(sleep, token).ConfigureAwait(false);
            }
            catch (RpcException ex)
            {
                throw SdkError.FromRpcException(ex);
            }
        }

        // Loop exited with the last exception still in flight: every
        // attempt was retryable but we exhausted MaxAttempts.
        throw SdkError.FromRpcException(lastError!);
    }

    private TimeSpan ResolveBackoff(RpcException ex, TimeSpan ceiling, TimeSpan previous)
    {
        if (_options.HonorRetryAfter)
        {
            // Server hints bypass MaxDelay per RFC 0006; the server speaks
            // with more authority about its own readiness than the local
            // ceiling does.
            var hint = SdkError.FromRpcException(ex).RetryAfter;
            if (hint is { } serverHint)
            {
                return serverHint;
            }
        }

        return _options.Strategy switch
        {
            RetryStrategy.Decorrelated => RetryPolicy.DecorrelatedDelay(
                previous,
                _options.BaseDelay,
                _options.MaxDelay,
                _options.DecorrelationFactor,
                _random),
            _ => RetryPolicy.FullDelay(
                ceiling,
                _options.MinDelay,
                _options.MaxDelay,
                _random),
        };
    }
}
