using System;
using Pinguteca.Sdk.Core.Retry;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Retry;

public sealed class RetryPolicyTests
{
    private static readonly TimeSpan _base = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan _max = TimeSpan.FromSeconds(30);

    [Test]
    public async Task FullDelay_ZeroFloor_DrawAtLowEndIsZero()
    {
        var random = new FixedRandom(0.0);

        var delay = RetryPolicy.FullDelay(_base, TimeSpan.Zero, _max, random);

        await Assert.That(delay).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task FullDelay_ZeroFloor_DrawAtHighEndApproachesCeiling()
    {
        var random = new FixedRandom(1.0 - 1e-9);

        var delay = RetryPolicy.FullDelay(_base, TimeSpan.Zero, _max, random);

        await Assert.That(delay.TotalMilliseconds).IsGreaterThanOrEqualTo(99.0);
        await Assert.That(delay.TotalMilliseconds).IsLessThanOrEqualTo(100.0);
    }

    [Test]
    public async Task FullDelay_HonoursMinDelayFloor()
    {
        var random = new FixedRandom(0.0);
        var floor = TimeSpan.FromMilliseconds(50);

        var delay = RetryPolicy.FullDelay(_base, floor, _max, random);

        await Assert.That(delay).IsEqualTo(floor);
    }

    [Test]
    public async Task FullDelay_FloorAboveCeilingCollapsesToFloor()
    {
        var random = new FixedRandom(1.0 - 1e-9);
        var floor = TimeSpan.FromMilliseconds(200);

        var delay = RetryPolicy.FullDelay(_base, floor, _max, random);

        await Assert.That(delay).IsEqualTo(floor);
    }

    [Test]
    public async Task FullDelay_RespectsMaxDelay()
    {
        var random = new FixedRandom(1.0 - 1e-9);
        var ceiling = TimeSpan.FromSeconds(60);
        var cap = TimeSpan.FromMilliseconds(500);

        var delay = RetryPolicy.FullDelay(ceiling, TimeSpan.Zero, cap, random);

        await Assert.That(delay.TotalMilliseconds).IsLessThanOrEqualTo(cap.TotalMilliseconds);
    }

    [Test]
    public async Task DecorrelatedDelay_DrawAtLowEndEqualsBase()
    {
        var random = new FixedRandom(0.0);

        var delay = RetryPolicy.DecorrelatedDelay(_base, _base, _max, 3.0, random);

        await Assert.That(delay).IsEqualTo(_base);
    }

    [Test]
    public async Task DecorrelatedDelay_DrawAtHighEndApproachesPrevTimesFactor()
    {
        var random = new FixedRandom(1.0 - 1e-9);
        var previous = TimeSpan.FromMilliseconds(200);

        var delay = RetryPolicy.DecorrelatedDelay(previous, _base, _max, 3.0, random);

        // upper = min(_max, 200 * 3) = 600ms; rand spans (base, upper).
        await Assert.That(delay.TotalMilliseconds).IsGreaterThanOrEqualTo(599.0);
        await Assert.That(delay.TotalMilliseconds).IsLessThanOrEqualTo(600.0);
    }

    [Test]
    public async Task DecorrelatedDelay_RespectsMaxDelay()
    {
        var random = new FixedRandom(1.0 - 1e-9);
        var previous = TimeSpan.FromSeconds(60);
        var cap = TimeSpan.FromMilliseconds(500);

        var delay = RetryPolicy.DecorrelatedDelay(previous, _base, cap, 3.0, random);

        await Assert.That(delay.TotalMilliseconds).IsLessThanOrEqualTo(cap.TotalMilliseconds);
    }

    [Test]
    public async Task GrowCeiling_MultipliesByFactor()
    {
        var grown = RetryPolicy.GrowCeiling(_base, 2.0, _max);

        await Assert.That(grown).IsEqualTo(TimeSpan.FromMilliseconds(200));
    }

    [Test]
    public async Task GrowCeiling_RespectsMaxDelay()
    {
        var grown = RetryPolicy.GrowCeiling(TimeSpan.FromSeconds(20), 2.0, _max);

        await Assert.That(grown).IsEqualTo(_max);
    }

    [Test]
    public async Task FullDelay_NullRandomThrows()
    {
        await Assert.That(() => RetryPolicy.FullDelay(_base, TimeSpan.Zero, _max, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task DecorrelatedDelay_NullRandomThrows()
    {
        await Assert.That(() => RetryPolicy.DecorrelatedDelay(_base, _base, _max, 3.0, null!))
            .ThrowsExactly<ArgumentNullException>();
    }
}

internal sealed class FixedRandom(double value) : IRetryRandom
{
    public double NextDouble() => value;
}
