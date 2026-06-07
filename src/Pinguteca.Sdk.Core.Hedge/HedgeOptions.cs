using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Core.Hedge;

/// <summary>
/// Knobs for <see cref="HedgeInterceptor"/>. Defaults mirror the
/// cross-SDK contract pinned in
/// <c>sdk-scaffold/docs/rfc/0013-hedged-requests.md</c>:
/// 3 total attempts at 50 ms stagger, NO_SIDE_EFFECTS gate enabled,
/// IDEMPOTENT opt-in via <see cref="HedgeIdempotent"/>.
/// </summary>
public sealed class HedgeOptions
{
    /// <summary>Default total attempts (primary plus two hedges).</summary>
    public const int DefaultMaxAttempts = 3;

    /// <summary>Default stagger between launches.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Total attempts including the primary. Must be at least 1; a
    /// value of 1 disables hedging and the interceptor passes through.
    /// </summary>
    public int MaxAttempts { get; init; } = DefaultMaxAttempts;

    /// <summary>
    /// Time between successive launches. Pick a value close to the
    /// typical P50 of the target RPC so hedges fire only when an
    /// attempt is definitely slow. Lowering amplifies load on the
    /// median case.
    /// </summary>
    public TimeSpan Delay { get; init; } = DefaultDelay;

    /// <summary>
    /// Enables hedging for methods marked <see cref="HedgeEligibility.Idempotent"/>.
    /// Default false because IDEMPOTENT only guarantees tolerance of
    /// duplicates, not that the duplicates are free; hedging an
    /// IDEMPOTENT mutation doubles billing-relevant load.
    /// </summary>
    public bool HedgeIdempotent { get; init; }

    /// <summary>
    /// Predicate returning the proto idempotency level of a procedure.
    /// The argument is the procedure path (<c>/service/method</c>).
    /// <para>
    /// .NET has no runtime access to the proto
    /// <c>idempotency_level</c> option, so this hook is the caller's
    /// way of supplying the same information. When this hook is null
    /// the interceptor treats every method as
    /// <see cref="HedgeEligibility.Unknown"/> and never hedges.
    /// </para>
    /// </summary>
    public Func<string, HedgeEligibility>? IsHedgeEligible { get; init; }

    /// <summary>
    /// Preferred clock injection point. When supplied the
    /// interceptor schedules its inter-hedge stagger through
    /// <see cref="Task.Delay(TimeSpan, System.TimeProvider, CancellationToken)"/>,
    /// so a <c>FakeTimeProvider</c> from
    /// <c>Microsoft.Extensions.TimeProvider.Testing</c> drives the
    /// stagger in tests.
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Legacy delay hook kept for source compatibility with callers
    /// that wired a function before <see cref="TimeProvider"/>
    /// existed. When both are null the interceptor uses
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; when
    /// both are set <see cref="DelayFunc"/> wins.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task>? DelayFunc { get; init; }
}
