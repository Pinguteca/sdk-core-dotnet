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
    /// Hook for deterministic time in tests. Production code leaves
    /// this null and the breaker uses <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    public Func<DateTimeOffset>? UtcNow { get; init; }
}
