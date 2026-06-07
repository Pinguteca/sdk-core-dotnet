using System;
using System.Collections.Generic;
using Grpc.Core;
using Pinguteca.Sdk.Core.Errors;

namespace Pinguteca.Sdk.Core.Retry;

/// <summary>
/// Selects the jitter scheme used to compute each retry delay.
/// Mirrors the strategies in sdk-core-go's <c>retry</c> package so
/// the behavioural contract from RFC 0002 stays identical across
/// SDKs.
/// </summary>
public enum RetryStrategy
{
    /// <summary>
    /// AWS "full jitter":
    /// <c>delay = MinDelay + rand(0, min(MaxDelay, ceiling) - MinDelay)</c>
    /// where <c>ceiling</c> grows by <see cref="RetryOptions.Multiplier"/>
    /// each retry. Default; best general-purpose choice.
    /// </summary>
    Full = 0,

    /// <summary>
    /// AWS "decorrelated jitter":
    /// <c>delay = BaseDelay + rand(0, min(MaxDelay, prev * DecorrelationFactor) - BaseDelay)</c>.
    /// Useful under sustained load where the attempt counter loses
    /// meaning. Ignores <see cref="RetryOptions.MinDelay"/>.
    /// </summary>
    Decorrelated = 1,
}

/// <summary>
/// Knobs for <see cref="RetryInterceptor"/>. Defaults mirror
/// sdk-core-go's <c>DefaultConfig</c>: 4 attempts, 100ms initial,
/// 30s max, full jitter, retry on Unavailable / ResourceExhausted /
/// Aborted / DeadlineExceeded, and honouring of <c>retry-after</c>
/// when present.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>Default 4 attempts total including the first call.</summary>
    public const int DefaultMaxAttempts = 4;

    /// <summary>Default starting backoff before the first retry.</summary>
    public static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromMilliseconds(100);

    /// <summary>Default upper bound on a single computed backoff.</summary>
    public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromSeconds(30);

    /// <summary>Default multiplicative growth of the ceiling for full jitter.</summary>
    public const double DefaultMultiplier = 2.0;

    /// <summary>Default scaling of previous delay for decorrelated jitter.</summary>
    public const double DefaultDecorrelationFactor = 3.0;

    /// <summary>Total attempts including the first call. Must be at least 1.</summary>
    public int MaxAttempts { get; init; } = DefaultMaxAttempts;

    /// <summary>
    /// Starting backoff. For full jitter this is the initial ceiling
    /// (the maximum the first retry may wait); for decorrelated jitter
    /// this is the floor of every draw.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = DefaultInitialBackoff;

    /// <summary>Upper bound on a single computed backoff.</summary>
    public TimeSpan MaxDelay { get; init; } = DefaultMaxBackoff;

    /// <summary>
    /// Optional floor for <see cref="RetryStrategy.Full"/>. When
    /// greater than zero the formula becomes
    /// <c>MinDelay + rand(0, max(0, ceiling - MinDelay))</c>.
    /// Default zero (classic AWS full jitter, allowing zero-wait
    /// retries). Ignored by <see cref="RetryStrategy.Decorrelated"/>.
    /// </summary>
    public TimeSpan MinDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Multiplicative growth of the ceiling for <see cref="RetryStrategy.Full"/>.
    /// Must be at least 1.0. Default 2.0.
    /// </summary>
    public double Multiplier { get; init; } = DefaultMultiplier;

    /// <summary>
    /// Scaling of the previous delay for <see cref="RetryStrategy.Decorrelated"/>.
    /// Must be at least 1.0. Default 3.0.
    /// </summary>
    public double DecorrelationFactor { get; init; } = DefaultDecorrelationFactor;

    /// <summary>Selects between full and decorrelated jitter. Default full.</summary>
    public RetryStrategy Strategy { get; init; } = RetryStrategy.Full;

    /// <summary>
    /// When true (the default), <see cref="SdkError.RetryAfter"/>
    /// from the server replaces the locally-computed backoff.
    /// </summary>
    public bool HonorRetryAfter { get; init; } = true;

    /// <summary>
    /// Predicate deciding whether a given gRPC status is retryable.
    /// Defaults to the generic set (Unavailable, ResourceExhausted,
    /// Aborted, DeadlineExceeded).
    /// </summary>
    public Func<StatusCode, bool> IsRetryable { get; init; } = DefaultIsRetryable;

    /// <summary>
    /// Optional injection seam for tests. Production code should
    /// leave this null so the interceptor uses the shared
    /// crypto-backed source.
    /// </summary>
    public IRetryRandom? Random { get; init; }

    /// <summary>
    /// Preferred clock injection point. When supplied the
    /// interceptor schedules retry waits through
    /// <see cref="Task.Delay(TimeSpan, System.TimeProvider, System.Threading.CancellationToken)"/>,
    /// so a <c>FakeTimeProvider</c> from
    /// <c>Microsoft.Extensions.TimeProvider.Testing</c> drives the
    /// retry waits in tests.
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Legacy delay hook kept for source compatibility with callers
    /// that wired a function before <see cref="TimeProvider"/>
    /// existed. When both are null the interceptor uses
    /// <see cref="Task.Delay(TimeSpan, System.Threading.CancellationToken)"/>;
    /// when both are set <see cref="Delay"/> wins (so callers can
    /// override the default without disturbing an injected
    /// <see cref="TimeProvider"/>).
    /// </summary>
    public Func<TimeSpan, System.Threading.CancellationToken, Task>? Delay { get; init; }

    private static readonly HashSet<StatusCode> DefaultRetryableSet =
    [
        StatusCode.Unavailable,
        StatusCode.ResourceExhausted,
        StatusCode.Aborted,
        StatusCode.DeadlineExceeded,
    ];

    private static bool DefaultIsRetryable(StatusCode code) => DefaultRetryableSet.Contains(code);
}

/// <summary>
/// Seam for deterministic jitter under test. The interceptor never
/// reads a Random instance directly so test code can swap a
/// counter-based implementation in.
/// </summary>
public interface IRetryRandom
{
    /// <summary>Returns a uniform value in <c>[0.0, 1.0)</c>.</summary>
    double NextDouble();
}
