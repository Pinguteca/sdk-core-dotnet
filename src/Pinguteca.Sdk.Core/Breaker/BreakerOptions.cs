using System;

namespace Pinguteca.Sdk.Core.Breaker;

/// <summary>
/// Knobs for <see cref="BreakerInterceptor"/>. Defaults align with
/// the cross-SDK numbers pinned in
/// sdk-scaffold/docs/rfc/0008-resilience-presets.md: 50% failure
/// rate over a 30-second window, minimum 20 samples, 5-second open
/// duration.
/// </summary>
public sealed class BreakerOptions
{
    public double FailureRateThreshold { get; init; } = 0.5;
    public TimeSpan WindowDuration { get; init; } = TimeSpan.FromSeconds(30);
    public int MinSamples { get; init; } = 20;
    public TimeSpan OpenDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Preferred clock injection point. When supplied the breaker
    /// reads <see cref="System.TimeProvider.GetUtcNow"/> on every
    /// transition decision; pair with <c>FakeTimeProvider</c> from
    /// <c>Microsoft.Extensions.TimeProvider.Testing</c> to drive
    /// state in tests.
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Legacy clock hook kept for source compatibility with callers
    /// that wired a function before <see cref="TimeProvider"/>
    /// existed. When both are null the breaker uses
    /// <see cref="DateTimeOffset.UtcNow"/>; when both are set
    /// <see cref="TimeProvider"/> wins.
    /// </summary>
    public Func<DateTimeOffset>? UtcNow { get; init; }
}
