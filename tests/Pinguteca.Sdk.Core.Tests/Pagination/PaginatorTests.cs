using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinguteca.Sdk.Core.Pagination;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Pagination;

public sealed class PaginatorTests
{
    [Test]
    public async Task IterAsync_SinglePageEmptyToken_YieldsItemsAndStops()
    {
        var fetch = StaticPages(("1,2,3", ""));

        var items = await DrainAsync(Paginator.IterAsync(fetch));

        await Assert.That(items).IsEquivalentTo(new[] { "1", "2", "3" });
    }

    [Test]
    public async Task IterAsync_MultiplePages_YieldsInOrder()
    {
        var fetch = StaticPages(
            ("a,b", "p2"),
            ("c", "p3"),
            ("d,e", ""));

        var items = await DrainAsync(Paginator.IterAsync(fetch));

        await Assert.That(items).IsEquivalentTo(new[] { "a", "b", "c", "d", "e" });
    }

    [Test]
    public async Task IterAsync_EmptyFirstPageWithoutToken_YieldsNothing()
    {
        var fetch = StaticPages(("", ""));

        var items = await DrainAsync(Paginator.IterAsync(fetch));

        await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task IterAsync_ErrorFromFetch_PropagatesAndTerminates()
    {
        var fetchCount = 0;
        FetchPageAsync<string> fetch = (_, _) =>
        {
            fetchCount++;
            throw new InvalidOperationException("boom");
        };

        await Assert.That(async () => await DrainAsync(Paginator.IterAsync(fetch)))
            .ThrowsExactly<InvalidOperationException>();
        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task IterAsync_Cancellation_HonouredBeforeNextFetch()
    {
        var cts = new CancellationTokenSource();
        var fetch = StaticPages(("a", "p2"), ("b", ""));

        var seen = new List<string>();
        await Assert.That(async () =>
        {
            await foreach (var item in Paginator.IterAsync(fetch, cts.Token))
            {
                seen.Add(item);
                cts.Cancel();
            }
        }).ThrowsExactly<OperationCanceledException>();

        await Assert.That(seen).IsEquivalentTo(new[] { "a" });
    }

    [Test]
    public async Task IterAsync_NullFetch_Throws()
    {
        await Assert.That(() => Paginator.IterAsync<string>(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task IterParallelAsync_LookaheadZero_DegradesToSequential()
    {
        var fetch = StaticPages(("x,y", "p2"), ("z", ""));

        var items = await DrainAsync(Paginator.IterParallelAsync(fetch, lookahead: 0));

        await Assert.That(items).IsEquivalentTo(new[] { "x", "y", "z" });
    }

    [Test]
    public async Task IterParallelAsync_PreservesPageOrder()
    {
        var fetch = StaticPages(
            ("1,2", "p2"),
            ("3,4", "p3"),
            ("5", ""));

        var items = await DrainAsync(Paginator.IterParallelAsync(fetch, lookahead: 4));

        await Assert.That(items).IsEquivalentTo(new[] { "1", "2", "3", "4", "5" });
    }

    [Test]
    public async Task IterParallelAsync_ErrorFromFetch_SurfacedAfterPriorPages()
    {
        var pages = new Queue<PageResult<string>>(
        [
            new(new[] { "a", "b" }, "p2"),
            new(new[] { "c" }, "p3"),
        ]);
        FetchPageAsync<string> fetch = (_, _) =>
        {
            if (pages.Count > 0)
            {
                return Task.FromResult(pages.Dequeue());
            }
            throw new InvalidOperationException("boom");
        };

        var seen = new List<string>();
        await Assert.That(async () =>
        {
            await foreach (var item in Paginator.IterParallelAsync(fetch, lookahead: 2))
            {
                seen.Add(item);
            }
        }).ThrowsExactly<InvalidOperationException>();

        await Assert.That(seen).IsEquivalentTo(new[] { "a", "b", "c" });
    }

    [Test]
    public async Task IterParallelAsync_Cancellation_StopsIteration()
    {
        var cts = new CancellationTokenSource();
        FetchPageAsync<string> fetch = async (token, ct) =>
        {
            await Task.Delay(50, ct).ConfigureAwait(false);
            return new PageResult<string>(new[] { token == string.Empty ? "first" : "next" }, "more");
        };

        var seen = new List<string>();
        await Assert.That(async () =>
        {
            await foreach (var item in Paginator.IterParallelAsync(fetch, lookahead: 2, cts.Token))
            {
                seen.Add(item);
                cts.Cancel();
            }
        }).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task IterParallelAsync_NullFetch_Throws()
    {
        await Assert.That(() => Paginator.IterParallelAsync<string>(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task CollectAsync_FullSuccess_ReturnsItemsAndNullError()
    {
        var fetch = StaticPages(("a,b", "p2"), ("c", ""));

        var result = await Paginator.CollectAsync(fetch);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Items).IsEquivalentTo(new[] { "a", "b", "c" });
    }

    [Test]
    public async Task CollectAsync_ErrorMidStream_ReturnsPartialAndError()
    {
        var pages = new Queue<PageResult<string>>(
        [
            new(new[] { "a" }, "p2"),
        ]);
        FetchPageAsync<string> fetch = (_, _) =>
        {
            if (pages.Count > 0)
            {
                return Task.FromResult(pages.Dequeue());
            }
            throw new InvalidOperationException("boom");
        };

        var result = await Paginator.CollectAsync(fetch);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsTypeOf<InvalidOperationException>();
        await Assert.That(result.Items).IsEquivalentTo(new[] { "a" });
    }

    [Test]
    public async Task CollectAsync_EnsureSuccess_ThrowsCapturedException()
    {
        FetchPageAsync<string> fetch = (_, _) => throw new InvalidOperationException("boom");

        var result = await Paginator.CollectAsync(fetch);

        await Assert.That(() => result.EnsureSuccess())
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task CollectAsync_NullFetch_Throws()
    {
        await Assert.That(async () => { await Paginator.CollectAsync<string>(null!); })
            .ThrowsExactly<ArgumentNullException>();
    }

    // ---------- helpers ----------

    private static FetchPageAsync<string> StaticPages(params (string Csv, string Next)[] pages)
    {
        var queue = new Queue<(string Csv, string Next)>(pages);
        return (_, _) =>
        {
            if (queue.Count == 0)
            {
                return Task.FromResult(new PageResult<string>(Array.Empty<string>(), string.Empty));
            }
            var (csv, next) = queue.Dequeue();
            var items = string.IsNullOrEmpty(csv)
                ? Array.Empty<string>()
                : csv.Split(',');
            return Task.FromResult(new PageResult<string>(items, next));
        };
    }

    private static async Task<List<T>> DrainAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source.ConfigureAwait(false))
        {
            items.Add(item);
        }
        return items;
    }
}
