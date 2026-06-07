using System;
using Pinguteca.Sdk.Core.Breaker;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Breaker;

public sealed class BreakerTimeProviderTests
{
    [Test]
    public async Task TimeProvider_DrivesOpenToHalfOpenTransition()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var breaker = new CircuitBreaker(new BreakerOptions
        {
            FailureRateThreshold = 0.5,
            MinSamples = 4,
            WindowDuration = TimeSpan.FromSeconds(30),
            OpenDuration = TimeSpan.FromSeconds(5),
            TimeProvider = clock,
        });

        // Trip with 4 consecutive failures (rate 1.0 over 4 samples).
        for (var i = 0; i < 4; i++) breaker.RecordFailure();
        await Assert.That(breaker.TryAcquire().Allow).IsFalse();

        // Still open one tick before the open duration expires.
        clock.Advance(TimeSpan.FromSeconds(4));
        await Assert.That(breaker.TryAcquire().Allow).IsFalse();

        // Past the open duration the breaker should permit the half-open probe.
        clock.Advance(TimeSpan.FromSeconds(2));
        var decision = breaker.TryAcquire();
        await Assert.That(decision.Allow).IsTrue();
        await Assert.That(decision.IsHalfOpenProbe).IsTrue();
    }

    [Test]
    public async Task TimeProvider_TakesPrecedenceOverLegacyUtcNowFunc()
    {
        // Both supplied; TimeProvider wins. The legacy func returns
        // a sentinel that, if read, would never satisfy the open
        // duration; the test only passes if the TimeProvider drives
        // the clock.
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        bool legacyCalled = false;
        var breaker = new CircuitBreaker(new BreakerOptions
        {
            FailureRateThreshold = 0.5,
            MinSamples = 2,
            OpenDuration = TimeSpan.FromSeconds(1),
            TimeProvider = clock,
            UtcNow = () => { legacyCalled = true; return DateTimeOffset.MinValue; },
        });

        breaker.RecordFailure();
        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(2));
        var decision = breaker.TryAcquire();

        await Assert.That(decision.Allow).IsTrue();
        await Assert.That(legacyCalled).IsFalse();
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public MutableTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
