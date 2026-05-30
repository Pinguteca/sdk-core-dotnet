using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Pinguteca.Sdk.Core.Caching;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Caching.Tests;

public sealed class MemoryCacheTests
{
    [Test]
    public async Task GetAsync_Miss_ReturnsNotFound()
    {
        var cache = new MemoryCache(8);
        var lookup = await cache.GetAsync("absent", CancellationToken.None);
        await Assert.That(lookup.Found).IsFalse();
    }

    [Test]
    public async Task Set_ThenGet_RoundTripsBody()
    {
        var cache = new MemoryCache(8);
        var entry = new Entry
        {
            Body = [1, 2, 3],
            Status = StatusCode.OK,
            Created = DateTimeOffset.UtcNow,
            Ttl = TimeSpan.FromMinutes(1),
        };
        await cache.SetAsync("k", entry, CancellationToken.None);
        var lookup = await cache.GetAsync("k", CancellationToken.None);
        await Assert.That(lookup.Found).IsTrue();
        await Assert.That(lookup.Entry!.Body).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Test]
    public async Task LruEvictsOldestOnCapacityExceeded()
    {
        var cache = new MemoryCache(2);
        foreach (var k in new[] { "a", "b", "c" })
        {
            await cache.SetAsync(k, new Entry { Body = [], Ttl = TimeSpan.FromMinutes(1), Created = DateTimeOffset.UtcNow },
                CancellationToken.None);
        }
        var a = await cache.GetAsync("a", CancellationToken.None);
        var c = await cache.GetAsync("c", CancellationToken.None);
        await Assert.That(a.Found).IsFalse();
        await Assert.That(c.Found).IsTrue();
    }

    [Test]
    public async Task GetAsync_PastSwrDeadline_Evicts()
    {
        var fixedNow = DateTimeOffset.UtcNow;
        var clock = fixedNow;
        var cache = new MemoryCache(8, () => clock);
        await cache.SetAsync("k", new Entry
        {
            Body = [],
            Created = fixedNow,
            Ttl = TimeSpan.FromSeconds(1),
            Swr = TimeSpan.FromSeconds(1),
        }, CancellationToken.None);

        clock = fixedNow.AddMilliseconds(1500);
        var stillFound = await cache.GetAsync("k", CancellationToken.None);
        await Assert.That(stillFound.Found).IsTrue();

        clock = fixedNow.AddSeconds(3);
        var gone = await cache.GetAsync("k", CancellationToken.None);
        await Assert.That(gone.Found).IsFalse();
    }

    [Test]
    public async Task DeleteAsync_RemovesEntry()
    {
        var cache = new MemoryCache(8);
        await cache.SetAsync("k", new Entry { Body = [], Ttl = TimeSpan.FromMinutes(1), Created = DateTimeOffset.UtcNow },
            CancellationToken.None);
        await cache.DeleteAsync("k", CancellationToken.None);
        var gone = await cache.GetAsync("k", CancellationToken.None);
        await Assert.That(gone.Found).IsFalse();
    }

    [Test]
    public async Task DeleteMatchingAsync_RemovesByPrefix()
    {
        var cache = new MemoryCache(8);
        foreach (var k in new[]
        {
            "tenantA:/svc/GetUser:h1",
            "tenantA:/svc/GetUser:h2",
            "tenantA:/svc/ListUsers:h3",
            "tenantB:/svc/GetUser:h4",
        })
        {
            await cache.SetAsync(k, new Entry { Body = [], Ttl = TimeSpan.FromMinutes(1), Created = DateTimeOffset.UtcNow },
                CancellationToken.None);
        }
        await cache.DeleteMatchingAsync("tenantA:/svc/GetUser:", CancellationToken.None);
        await Assert.That((await cache.GetAsync("tenantA:/svc/GetUser:h1", CancellationToken.None)).Found).IsFalse();
        await Assert.That((await cache.GetAsync("tenantA:/svc/ListUsers:h3", CancellationToken.None)).Found).IsTrue();
        await Assert.That((await cache.GetAsync("tenantB:/svc/GetUser:h4", CancellationToken.None)).Found).IsTrue();
    }

    [Test]
    public async Task DeleteMatchingAsync_EmptyPrefixThrows()
    {
        var cache = new MemoryCache(8);
        await Assert.That(async () => await cache.DeleteMatchingAsync("", CancellationToken.None))
            .ThrowsExactly<ArgumentException>();
    }
}
