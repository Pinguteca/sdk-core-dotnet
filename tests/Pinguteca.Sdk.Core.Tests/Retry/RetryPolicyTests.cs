using System;
using Pinguteca.Sdk.Core.Retry;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Retry;

public sealed class RetryPolicyTests
{
    [Test]
    public async Task NextDelay_FirstRetry_DrawsFromBaseRange()
    {
        var random = new FixedRandom(0.0);
        var baseDelay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromSeconds(5);

        var delay = RetryPolicy.NextDelay(TimeSpan.Zero, baseDelay, maxDelay, random);

        await Assert.That(delay).IsEqualTo(baseDelay);
    }

    [Test]
    public async Task NextDelay_GrowsByCubingPrevious()
    {
        var random = new FixedRandom(1.0 - 1e-9);
        var baseDelay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromSeconds(60);

        var delay = RetryPolicy.NextDelay(TimeSpan.FromMilliseconds(200), baseDelay, maxDelay, random);

        // With random ~1.0, the ceiling (previous*3) dominates the draw.
        await Assert.That(delay.TotalMilliseconds).IsGreaterThanOrEqualTo(599);
        await Assert.That(delay.TotalMilliseconds).IsLessThanOrEqualTo(601);
    }

    [Test]
    public async Task NextDelay_RespectsMaxDelay()
    {
        var random = new FixedRandom(1.0 - 1e-9);
        var baseDelay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromMilliseconds(500);

        var delay = RetryPolicy.NextDelay(TimeSpan.FromSeconds(10), baseDelay, maxDelay, random);

        await Assert.That(delay).IsEqualTo(maxDelay);
    }

    [Test]
    public async Task NextDelay_NullRandomThrows()
    {
        await Assert.That(() => RetryPolicy.NextDelay(
            TimeSpan.Zero, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1), null!))
            .ThrowsExactly<ArgumentNullException>();
    }
}

internal sealed class FixedRandom(double value) : IRetryRandom
{
    public double NextDouble() => value;
}
