using System;
using System.Security.Cryptography;

namespace Pinguteca.Sdk.Core.Retry;

/// <summary>
/// Pure backoff computation extracted from
/// <see cref="RetryInterceptor"/> so it can be unit-tested without
/// a real RPC. Implements the two jitter schemes from sdk-core-go
/// so the behavioural contract is identical across SDKs.
/// </summary>
internal static class RetryPolicy
{
    /// <summary>
    /// AWS "full jitter":
    /// <c>delay = MinDelay + rand(0, max(0, min(MaxDelay, ceiling) - MinDelay))</c>.
    /// Allows zero-wait retries when <see cref="RetryOptions.MinDelay"/>
    /// is zero (the default), which de-synchronises retry storms
    /// most aggressively.
    /// </summary>
    public static TimeSpan FullDelay(
        TimeSpan ceiling,
        TimeSpan minDelay,
        TimeSpan maxDelay,
        IRetryRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var upper = ceiling > maxDelay ? maxDelay : ceiling;
        if (minDelay <= TimeSpan.Zero)
        {
            var draw = (long)(upper.Ticks * random.NextDouble());
            return TimeSpan.FromTicks(draw);
        }
        var spreadTicks = upper.Ticks - minDelay.Ticks;
        if (spreadTicks <= 0)
        {
            return minDelay;
        }
        var jitter = (long)(spreadTicks * random.NextDouble());
        return TimeSpan.FromTicks(minDelay.Ticks + jitter);
    }

    /// <summary>
    /// AWS "decorrelated jitter":
    /// <c>delay = BaseDelay + rand(0, max(0, min(MaxDelay, prev * DecorrelationFactor) - BaseDelay))</c>.
    /// Bounds the next delay relative to the previous one rather
    /// than to an attempt counter.
    /// </summary>
    public static TimeSpan DecorrelatedDelay(
        TimeSpan previous,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        double decorrelationFactor,
        IRetryRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var scaled = TimeSpan.FromTicks((long)(previous.Ticks * decorrelationFactor));
        var upper = scaled > maxDelay ? maxDelay : scaled;
        var spreadTicks = upper.Ticks - baseDelay.Ticks;
        if (spreadTicks <= 0)
        {
            return baseDelay;
        }
        var jitter = (long)(spreadTicks * random.NextDouble());
        return TimeSpan.FromTicks(baseDelay.Ticks + jitter);
    }

    /// <summary>
    /// Grow the full-jitter ceiling by <paramref name="multiplier"/>,
    /// capped at <paramref name="maxDelay"/>. The result feeds the
    /// next call to <see cref="FullDelay"/>.
    /// </summary>
    public static TimeSpan GrowCeiling(TimeSpan ceiling, double multiplier, TimeSpan maxDelay)
    {
        var grown = TimeSpan.FromTicks((long)(ceiling.Ticks * multiplier));
        return grown > maxDelay ? maxDelay : grown;
    }
}

internal sealed class CryptoRetryRandom : IRetryRandom
{
    public static readonly CryptoRetryRandom Instance = new();

    // Cross-SDK contract pinned in sdk-scaffold/docs/rfc/0007-random-source-policy.md:
    // CSPRNG-only randomness, top-53-bits / 2^53 recipe, 0.5 fallback on
    // entropy starvation. Mirrors sdk-core-go's DefaultJitterSource.
    public double NextDouble()
    {
        Span<byte> buffer = stackalloc byte[8];
        try
        {
            RandomNumberGenerator.Fill(buffer);
        }
        catch (CryptographicException)
        {
            // Boot-time entropy starvation is the only realistic failure
            // mode on Windows/Linux/macOS. RFC 0007 mandates mid-jitter
            // rather than propagating: backoff that panics on boot is
            // the opposite of what backoff is for.
            return 0.5;
        }
        var bits = BitConverter.ToUInt64(buffer) >> 11;
        return bits / (double)(1UL << 53);
    }
}
