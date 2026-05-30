using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Ergo;

/// <summary>
/// Snapshot of a long-running operation. Poll implementations
/// return <see cref="Done"/> = true with <see cref="Result"/> (and
/// optionally <see cref="Error"/> for terminal failure), or
/// <see cref="Done"/> = false to request another poll.
/// <see cref="RetryAfter"/> overrides the local backoff when
/// positive, mirroring RFC 0006's server-supplied retry-after
/// handling.
/// </summary>
public sealed record OperationStatus<T>(
    bool Done,
    T? Result = default,
    Exception? Error = null,
    TimeSpan RetryAfter = default);

/// <summary>
/// Long-running operation handle the Layer 1.5 resource method
/// returns. Consumers either poll manually via <see cref="Poll"/>
/// or block via <see cref="WaitAsync"/>.
/// </summary>
public sealed class Operation<T>
{
    /// <summary>
    /// Default first wait between polls when <see cref="InitialDelay"/>
    /// is zero.
    /// </summary>
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Default backoff ceiling when <see cref="MaxDelay"/> is zero.
    /// </summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default growth multiplier when <see cref="Multiplier"/> is
    /// less than 1.
    /// </summary>
    public const double DefaultMultiplier = 2.0;

    /// <summary>Server-side operation identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Fetches the current status from the server. Required.
    /// </summary>
    public Func<CancellationToken, Task<OperationStatus<T>>> Poll { get; init; } = null!;

    /// <summary>
    /// First wait between polls. Zero defaults to
    /// <see cref="DefaultInitialDelay"/>.
    /// </summary>
    public TimeSpan InitialDelay { get; init; }

    /// <summary>
    /// Backoff ceiling. Zero defaults to <see cref="DefaultMaxDelay"/>.
    /// </summary>
    public TimeSpan MaxDelay { get; init; }

    /// <summary>
    /// Growth multiplier between polls. Values less than 1 default
    /// to <see cref="DefaultMultiplier"/>.
    /// </summary>
    public double Multiplier { get; init; }

    /// <summary>
    /// Test seam for the sleep between polls. Production code
    /// leaves this null so <see cref="WaitAsync"/> uses
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task>? SleepAsync { get; init; }

    /// <summary>
    /// Polls until the operation reports <see cref="OperationStatus{T}.Done"/>
    /// or the cancellation token fires. Total wait budget is bounded
    /// by the token; per-poll timeouts come from the underlying RPC's
    /// own deadline (typically the Layer 2 timeout interceptor's
    /// default).
    /// </summary>
    public async Task<T> WaitAsync(CancellationToken cancellationToken)
    {
        if (Poll is null)
        {
            throw new InvalidOperationException("ergo: Operation requires a Poll function");
        }

        var (initial, maxDelay, mult) = Tuning();
        var sleep = SleepAsync ?? DefaultSleepAsync;

        var ceiling = initial;
        while (true)
        {
            var status = await Poll(cancellationToken).ConfigureAwait(false);
            if (status.Done)
            {
                if (status.Error is not null)
                {
                    throw status.Error;
                }
                return status.Result!;
            }
            var wait = NextDelay(ceiling, status.RetryAfter);
            await sleep(wait, cancellationToken).ConfigureAwait(false);
            ceiling = Grow(ceiling, mult, maxDelay);
        }
    }

    private (TimeSpan initial, TimeSpan maxDelay, double mult) Tuning()
    {
        var initial = InitialDelay <= TimeSpan.Zero ? DefaultInitialDelay : InitialDelay;
        var maxDelay = MaxDelay <= TimeSpan.Zero ? DefaultMaxDelay : MaxDelay;
        var mult = Multiplier < 1 ? DefaultMultiplier : Multiplier;
        return (initial, maxDelay, mult);
    }

    private static TimeSpan NextDelay(TimeSpan ceiling, TimeSpan serverHint)
    {
        if (serverHint > TimeSpan.Zero)
        {
            return serverHint;
        }
        if (ceiling <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }
        // Full-jitter draw scaled by ceiling. Uses crypto/rand via
        // RandomNumberGenerator; FIPS-approved per the cross-SDK
        // policy.
        var draw = JitterDraw(ceiling.Ticks);
        return TimeSpan.FromTicks(draw);
    }

    private static TimeSpan Grow(TimeSpan current, double multiplier, TimeSpan max)
    {
        var nextTicks = (long)(current.Ticks * multiplier);
        return nextTicks > max.Ticks ? max : TimeSpan.FromTicks(nextTicks);
    }

    // Top-53-bits / 2^53 recipe constants for the uniform jitter
    // draw. Mirrors sdk-core-go's full-jitter implementation per
    // RFC 0007.
    private const int JitterShiftBits = 11;       // 64 - 53
    private const ulong JitterMantissa = 1UL << 53;
    private const int MidJitterDivisor = 2;

    private static long JitterDraw(long upper)
    {
        if (upper <= 0)
        {
            return 0;
        }
        Span<byte> buffer = stackalloc byte[8];
        try
        {
            RandomNumberGenerator.Fill(buffer);
        }
        catch (CryptographicException)
        {
            // Boot-time entropy starvation; return mid-jitter rather
            // than block the polling loop.
            return upper / MidJitterDivisor;
        }
        var bits = BinaryPrimitives.ReadUInt64BigEndian(buffer) >> JitterShiftBits;
        var frac = bits / (double)JitterMantissa;
        return (long)(frac * upper);
    }

    private static async Task DefaultSleepAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }
}
