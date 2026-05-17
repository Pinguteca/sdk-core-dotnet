using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Pinguteca.Sdk.Core.Hedge;

/// <summary>
/// gRPC client interceptor that races multiple parallel attempts of
/// the same RPC and returns the first successful response,
/// cancelling the others.
///
/// Cross-SDK contract pinned in
/// <c>sdk-scaffold/docs/rfc/0013-hedged-requests.md</c>:
/// opt-in only (never in <c>presets.Standalone</c> or
/// <c>presets.Mesh</c>); default scope hedges only methods marked
/// <c>NO_SIDE_EFFECTS</c> in the schema; IDEMPOTENT methods are
/// skipped unless <see cref="HedgeOptions.HedgeIdempotent"/> is
/// true; first-success-wins; on all-fail the LAST observed error is
/// returned, not the first.
///
/// Chain placement: inside Retry, inside Rotation, outside Auth.
/// Each hedge attempt is a retry attempt from the outer retry
/// interceptor's point of view, so retry's <c>MaxAttempts</c> must
/// be lowered to keep total request volume bounded by
/// <c>retry.MaxAttempts * hedge.MaxAttempts</c>.
///
/// Streaming RPCs pass through unchanged: streams cannot be replayed
/// safely without a per-message reconciliation primitive.
/// </summary>
public sealed class HedgeInterceptor : Interceptor
{
    private readonly HedgeOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public HedgeInterceptor(HedgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxAttempts < 1)
        {
            throw new ArgumentException("MaxAttempts must be at least 1.", nameof(options));
        }
        if (options.Delay < TimeSpan.Zero)
        {
            throw new ArgumentException("Delay must be non-negative.", nameof(options));
        }
        _options = options;
        _delay = options.DelayFunc ?? Task.Delay;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        if (_options.MaxAttempts <= 1 || !ShouldHedge(context))
        {
            return continuation(request, context);
        }

        var response = RaceAsync(request, context, continuation);
        return new AsyncUnaryCall<TResponse>(
            response,
            response.ContinueWith(_ => new Metadata(), TaskScheduler.Default),
            () => response.IsCompletedSuccessfully ? Status.DefaultSuccess : Status.DefaultCancelled,
            () => [],
            () => { });
    }

    private bool ShouldHedge<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (_options.IsHedgeEligible is null)
        {
            return false;
        }
        var procedure = $"/{context.Method.ServiceName}/{context.Method.Name}";
        return _options.IsHedgeEligible(procedure) switch
        {
            HedgeEligibility.NoSideEffects => true,
            HedgeEligibility.Idempotent => _options.HedgeIdempotent,
            _ => false,
        };
    }

    private async Task<TResponse> RaceAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Options.CancellationToken);
        var results = Channel.CreateUnbounded<Result<TResponse>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var attemptContext = WithCancellation(context, cts.Token);

        int launched = 0;
        int completed = 0;
        Exception? lastError = null;

        Fire(request, attemptContext, continuation, results.Writer);
        launched++;

        while (completed < launched || launched < _options.MaxAttempts)
        {
            var resultTask = results.Reader.WaitToReadAsync(cts.Token).AsTask();

            Task waitTask;
            if (launched < _options.MaxAttempts)
            {
                waitTask = _delay(_options.Delay, cts.Token);
            }
            else
            {
                waitTask = resultTask;
            }

            var done = await Task.WhenAny(resultTask, waitTask).ConfigureAwait(false);

            if (done == resultTask && resultTask.IsCompletedSuccessfully && resultTask.Result &&
                results.Reader.TryRead(out var r))
            {
                completed++;
                if (r.Error is null)
                {
                    cts.Cancel();
                    return r.Response!;
                }
                lastError = r.Error;
                continue;
            }

            if (launched < _options.MaxAttempts)
            {
                Fire(request, attemptContext, continuation, results.Writer);
                launched++;
            }
        }

        cts.Cancel();
        throw lastError ?? new InvalidOperationException("hedge: no result and no error");
    }

    private static void Fire<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
        ChannelWriter<Result<TResponse>> writer)
        where TRequest : class
        where TResponse : class
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var resp = await continuation(request, context).ResponseAsync.ConfigureAwait(false);
                await writer.WriteAsync(new Result<TResponse>(resp, null)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await writer.WriteAsync(new Result<TResponse>(default, ex)).ConfigureAwait(false);
            }
        });
    }

    private static ClientInterceptorContext<TRequest, TResponse> WithCancellation<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        CancellationToken token)
        where TRequest : class
        where TResponse : class
    {
        var newOptions = context.Options.WithCancellationToken(token);
        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            newOptions);
    }

    private readonly record struct Result<TResponse>(TResponse? Response, Exception? Error);
}
