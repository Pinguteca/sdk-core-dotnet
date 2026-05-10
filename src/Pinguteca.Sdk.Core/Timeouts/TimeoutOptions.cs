using System;

namespace Pinguteca.Sdk.Core.Timeouts;

/// <summary>
/// Configures <see cref="TimeoutInterceptor"/>. The interceptor
/// stamps a default deadline on every unary call unless the caller
/// already set <see cref="Grpc.Core.CallOptions.Deadline"/> on the
/// outgoing call. Streaming calls pass through; deadline semantics
/// for long-lived streams are caller-driven.
/// </summary>
public sealed class TimeoutOptions
{
    /// <summary>
    /// Per-call deadline applied when the caller has not provided
    /// one. Null disables the default and leaves each call's
    /// deadline up to the caller.
    /// </summary>
    public TimeSpan? Default { get; init; }
}
