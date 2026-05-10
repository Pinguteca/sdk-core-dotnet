using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Errors;

namespace Pinguteca.Sdk.Core.Retry;

/// <summary>
/// gRPC client interceptor that retries failed unary calls using
/// decorrelated jitter and an optional server-supplied
/// <c>retry-after</c> hint. Streaming calls pass through; replaying
/// a stream is unsafe without server cooperation that this layer
/// does not assume.
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
        _random = _options.Random ?? CryptoRetryRandom.Instance;
        _delay = _options.Delay ?? Task.Delay;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var response = RunWithRetryAsync(request, context, continuation);
        return new AsyncUnaryCall<TResponse>(
            response,
            ResponseHeadersTaskFromAsync(response),
            () => StatusFromAsync(response),
            () => TrailersFromAsync(response),
            () => { /* cancellation handled by the underlying call */ });
    }

    private async Task<TResponse> RunWithRetryAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        var token = context.Options.CancellationToken;
        var previousDelay = TimeSpan.Zero;
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
                var sleep = ResolveBackoff(ex, previousDelay);
                previousDelay = sleep;
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

    private TimeSpan ResolveBackoff(RpcException ex, TimeSpan previous)
    {
        if (_options.HonorRetryAfter)
        {
            var hint = SdkError.FromRpcException(ex).RetryAfter;
            if (hint is { } serverHint)
            {
                return serverHint;
            }
        }
        return RetryPolicy.NextDelay(previous, _options.BaseDelay, _options.MaxDelay, _random);
    }

    private static Task<Metadata> ResponseHeadersTaskFromAsync<TResponse>(Task<TResponse> source)
        => source.ContinueWith(
            _ => new Metadata(),
            TaskScheduler.Default);

    private static Status StatusFromAsync<TResponse>(Task<TResponse> source)
        => source.IsCompletedSuccessfully ? Status.DefaultSuccess : Status.DefaultCancelled;

    private static Metadata TrailersFromAsync<TResponse>(Task<TResponse> source) => [];
}
