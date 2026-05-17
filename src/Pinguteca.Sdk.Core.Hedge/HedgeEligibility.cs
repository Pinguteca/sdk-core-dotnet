namespace Pinguteca.Sdk.Core.Hedge;

/// <summary>
/// Tri-state result returned by
/// <see cref="HedgeOptions.IsHedgeEligible"/>. Mirrors the proto
/// <c>idempotency_level</c> option so the interceptor can apply
/// RFC 0013's safety gate (NO_SIDE_EFFECTS always eligible,
/// IDEMPOTENT eligible only when
/// <see cref="HedgeOptions.HedgeIdempotent"/> is true, Unknown
/// never hedged).
/// </summary>
public enum HedgeEligibility
{
    /// <summary>Method idempotency is unknown. Never hedged.</summary>
    Unknown = 0,

    /// <summary>
    /// Method declared <c>NO_SIDE_EFFECTS</c> in the schema. Always
    /// eligible for hedging.
    /// </summary>
    NoSideEffects = 1,

    /// <summary>
    /// Method declared <c>IDEMPOTENT</c> in the schema. Eligible only
    /// when <see cref="HedgeOptions.HedgeIdempotent"/> is true,
    /// because IDEMPOTENT guarantees tolerance of duplicates but not
    /// that they are cheap.
    /// </summary>
    Idempotent = 2,
}
