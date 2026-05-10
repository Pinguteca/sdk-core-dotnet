using System;
using System.Security.Cryptography;

namespace Pinguteca.Sdk.Core.Retry;

/// <summary>
/// Pure backoff computation extracted from
/// <see cref="RetryInterceptor"/> so it can be unit-tested without
/// a real RPC.
/// </summary>
internal static class RetryPolicy
{
    /// <summary>
    /// Decorrelated jitter as published by AWS. Each step samples
    /// uniformly in <c>[base, previous * 3]</c>, capped at
    /// <paramref name="maxDelay"/>. The algorithm avoids the
    /// thundering-herd pathologies of pure exponential backoff
    /// while bounding the worst-case wait.
    /// </summary>
    /// <param name="previous">Last delay actually waited; pass
    /// <see cref="TimeSpan.Zero"/> on the first retry.</param>
    /// <param name="baseDelay">Floor of the random draw.</param>
    /// <param name="maxDelay">Cap on the returned value.</param>
    /// <param name="random">Source of <c>[0,1)</c> doubles.</param>
    public static TimeSpan NextDelay(
        TimeSpan previous,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        IRetryRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var floor = baseDelay.Ticks;
        var ceiling = Math.Max(previous.Ticks * 3L, floor);
        var draw = floor + (long)((ceiling - floor) * random.NextDouble());
        var capped = Math.Min(draw, maxDelay.Ticks);
        return TimeSpan.FromTicks(capped);
    }
}

internal sealed class CryptoRetryRandom : IRetryRandom
{
    public static readonly CryptoRetryRandom Instance = new();

    public double NextDouble()
    {
        // 53-bit mantissa of double; mirrors the canonical
        // "ulong >> 11, divide by 2^53" recipe so the distribution
        // is uniform on [0, 1) even at the edges. RandomNumberGenerator
        // is FIPS-compliant per the SDK feedback rule.
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);
        var bits = BitConverter.ToUInt64(buffer) >> 11;
        return bits / (double)(1UL << 53);
    }
}
