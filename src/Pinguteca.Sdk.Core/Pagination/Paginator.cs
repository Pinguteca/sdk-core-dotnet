using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Core.Pagination;

/// <summary>
/// Consumer-side iterators for token-paginated RPCs. Behavioural
/// contract pinned in
/// <c>sdk-scaffold/docs/rfc/0009-pagination-api-shape.md</c>:
/// <list type="bullet">
/// <item>Empty next-token terminates iteration.</item>
/// <item>Cancellation is honoured via the supplied
/// <see cref="CancellationToken"/>.</item>
/// <item>Errors terminate iteration; the next iteration call
/// returns done.</item>
/// <item>Page order is preserved; <see cref="IterParallelAsync"/>
/// runs the producer ahead of the consumer but never reorders
/// pages.</item>
/// <item><see cref="CollectAsync"/> returns partial-on-error.</item>
/// </list>
/// Pagination is a Layer 2 helper, not an interceptor: each
/// underlying RPC still traverses the full interceptor chain.
/// </summary>
public static class Paginator
{
    /// <summary>
    /// Recommended buffer for <see cref="IterParallelAsync"/> when
    /// the caller does not pick its own. Two pages of headroom
    /// amortises typical per-fetch latency without ballooning memory
    /// for large pages.
    /// </summary>
    public const int DefaultLookahead = 2;

    /// <summary>
    /// Yields every item across every page. Iteration stops on the
    /// first error from <paramref name="fetch"/>, on cancellation, or
    /// when the next-page token is empty.
    /// </summary>
    public static IAsyncEnumerable<T> IterAsync<T>(
        FetchPageAsync<T> fetch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        return IterAsyncCore(fetch, cancellationToken);
    }

    private static async IAsyncEnumerable<T> IterAsyncCore<T>(
        FetchPageAsync<T> fetch,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var token = string.Empty;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetch(token, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                yield return item;
            }
            if (string.IsNullOrEmpty(page.NextPageToken))
            {
                yield break;
            }
            token = page.NextPageToken;
        }
    }

    /// <summary>
    /// Like <see cref="IterAsync"/> but a background producer fetches
    /// pages ahead of the consumer up to <paramref name="lookahead"/>.
    /// Pages are yielded in order; an error from page N is surfaced
    /// after all items from pages 0..N-1 have been yielded.
    /// <paramref name="lookahead"/> less than 1 falls back to
    /// <see cref="IterAsync"/>.
    /// </summary>
    public static IAsyncEnumerable<T> IterParallelAsync<T>(
        FetchPageAsync<T> fetch,
        int lookahead = DefaultLookahead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        if (lookahead < 1)
        {
            return IterAsyncCore(fetch, cancellationToken);
        }
        return IterParallelAsyncCore(fetch, lookahead, cancellationToken);
    }

    private static async IAsyncEnumerable<T> IterParallelAsyncCore<T>(
        FetchPageAsync<T> fetch,
        int lookahead,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<PageEntry<T>>(new BoundedChannelOptions(lookahead)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        using var producerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = Task.Run(
            () => RunProducerAsync(fetch, channel.Writer, producerCts.Token),
            producerCts.Token);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (entry.Error is not null)
                {
                    ExceptionDispatchInfo.Capture(entry.Error).Throw();
                }
                foreach (var item in entry.Page!.Items)
                {
                    yield return item;
                }
            }
        }
        finally
        {
            producerCts.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch
            {
                // Producer tear-down errors are swallowed; the meaningful
                // exception is whichever one we already re-threw above
                // (or the consumer's cancellation), and producer state is
                // not observable beyond this method.
            }
        }
    }

    private static async Task RunProducerAsync<T>(
        FetchPageAsync<T> fetch,
        ChannelWriter<PageEntry<T>> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = string.Empty;
            while (!cancellationToken.IsCancellationRequested)
            {
                PageResult<T> page;
                try
                {
                    page = await fetch(token, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    await writer.WriteAsync(new PageEntry<T>(null, ex), CancellationToken.None)
                        .ConfigureAwait(false);
                    return;
                }

                try
                {
                    await writer.WriteAsync(new PageEntry<T>(page, null), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (string.IsNullOrEmpty(page.NextPageToken))
                {
                    return;
                }
                token = page.NextPageToken;
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Materialises every item from <paramref name="fetch"/> into a
    /// <see cref="CollectResult{T}"/>. On error or cancellation,
    /// returns the items collected so far plus the exception. Callers
    /// who need all-or-nothing semantics use
    /// <see cref="CollectResult{T}.EnsureSuccess"/>.
    /// </summary>
    public static async Task<CollectResult<T>> CollectAsync<T>(
        FetchPageAsync<T> fetch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        var items = new List<T>();
        try
        {
            await foreach (var item in IterAsyncCore(fetch, cancellationToken).ConfigureAwait(false))
            {
                items.Add(item);
            }
            return new CollectResult<T>(items, null);
        }
        catch (Exception ex)
        {
            return new CollectResult<T>(items, ex);
        }
    }

    private readonly record struct PageEntry<T>(PageResult<T>? Page, Exception? Error);
}
