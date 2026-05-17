using System;

namespace Pinguteca.Sdk.Core.Auth;

/// <summary>
/// Knobs for <see cref="RotationInterceptor"/>. Defaults mirror the
/// cross-SDK contract pinned in
/// <c>sdk-scaffold/docs/rfc/0012-token-rotation-policy.md</c>:
/// one-shot retry on <c>Unauthenticated</c>, safety gate enabled by
/// default, schema-blind .NET falls back to the caller-supplied
/// <see cref="IsIdempotent"/> predicate.
/// </summary>
public sealed class RotationOptions
{
    /// <summary>
    /// Token source whose cache the interceptor invalidates on a
    /// rotation event. Required.
    /// </summary>
    public IRotatingTokenSource Source { get; init; } = null!;

    /// <summary>
    /// Disables the idempotency safety gate. Default false.
    /// <para>
    /// When false, the interceptor skips rotation+retry for methods
    /// the <see cref="IsIdempotent"/> predicate marks as
    /// non-idempotent. The original RPC may have been processed
    /// server-side before <c>Unauthenticated</c> came back, and a
    /// blind retry could create a duplicate write.
    /// </para>
    /// <para>
    /// Set true only when paired with the idempotency-key interceptor
    /// and a server that deduplicates by key.
    /// </para>
    /// </summary>
    public bool AllowNonIdempotent { get; init; }

    /// <summary>
    /// Predicate identifying methods safe to rotate on. The argument
    /// is the procedure path (<c>/service/method</c>) and the return
    /// is <c>true</c> when the method is idempotent or
    /// <c>NO_SIDE_EFFECTS</c>.
    /// <para>
    /// .NET has no runtime access to the proto <c>idempotency_level</c>
    /// option (gRPC-Net does not surface it), so this hook is the
    /// caller's way of supplying the same information. Per RFC 0006,
    /// when this hook is null the SDK defaults to
    /// <see cref="AllowNonIdempotent"/> = true behaviour and the
    /// README documents the divergence; consumers who care wire the
    /// hook.
    /// </para>
    /// </summary>
    public Func<string, bool>? IsIdempotent { get; init; }
}
