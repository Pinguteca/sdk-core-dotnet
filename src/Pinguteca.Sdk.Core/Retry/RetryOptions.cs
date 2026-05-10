using System;
using System.Collections.Generic;
using Grpc.Core;
using Pinguteca.Sdk.Core.Errors;

namespace Pinguteca.Sdk.Core.Retry;

/// <summary>
/// Knobs for <see cref="RetryInterceptor"/>. The defaults are tuned
/// for the canonical Connect/gRPC interaction: 3 attempts total,
/// 100 ms base delay, 5 s cap, the retryable status set from
/// <see cref="SdkError.IsRetryable"/>, and honouring of
/// <c>retry-after</c> when present.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// Total attempts including the first call. Must be at least 1.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Initial backoff before the first retry. Subsequent retries
    /// use decorrelated jitter seeded from this value.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Upper bound on a single computed backoff. Honours a server-
    /// supplied <c>retry-after</c> independently; this cap only
    /// constrains the locally-computed exponential value.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When true (the default), <see cref="SdkError.RetryAfter"/>
    /// from the server replaces the locally-computed backoff. When
    /// false, the server hint is ignored and only the exponential
    /// jitter is used.
    /// </summary>
    public bool HonorRetryAfter { get; init; } = true;

    /// <summary>
    /// Predicate that decides whether a given gRPC status is
    /// retryable. Defaults to the generic set
    /// (<see cref="SdkError.IsRetryable"/>): Unavailable,
    /// DeadlineExceeded, ResourceExhausted, Aborted.
    /// </summary>
    public Func<StatusCode, bool> IsRetryable { get; init; } = DefaultIsRetryable;

    /// <summary>
    /// Optional injection seam for tests. Production code should
    /// leave this null so the interceptor uses the shared
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/>.
    /// </summary>
    public IRetryRandom? Random { get; init; }

    /// <summary>
    /// Optional delay injector. Production code should leave this
    /// null so the interceptor uses <see cref="Task.Delay(TimeSpan, System.Threading.CancellationToken)"/>.
    /// </summary>
    public Func<TimeSpan, System.Threading.CancellationToken, Task>? Delay { get; init; }

    private static readonly HashSet<StatusCode> DefaultRetryableSet =
    [
        StatusCode.Unavailable,
        StatusCode.DeadlineExceeded,
        StatusCode.ResourceExhausted,
        StatusCode.Aborted,
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
    /// <summary>
    /// Returns a value in <c>[0.0, 1.0)</c>.
    /// </summary>
    double NextDouble();
}
