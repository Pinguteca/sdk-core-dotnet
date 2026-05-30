using System;
using System.Collections.Generic;

namespace Pinguteca.Sdk.Core.Caching;

/// <summary>
/// Cache policy for a single procedure. Read methods set
/// <see cref="Ttl"/> &gt; 0 to enable caching; write methods leave
/// <see cref="Ttl"/> at zero and populate <see cref="Invalidates"/>
/// with the read-method names whose cache entries should be flushed
/// on a successful invocation.
/// </summary>
public sealed class CacheSpec
{
    /// <summary>
    /// Cache-hit window after a successful response. Zero disables
    /// caching for the method.
    /// </summary>
    public TimeSpan Ttl { get; init; }

    /// <summary>
    /// Stale-while-revalidate window beyond <see cref="Ttl"/>. Zero
    /// disables SWR.
    /// </summary>
    public TimeSpan Swr { get; init; }

    /// <summary>
    /// Cache-hit window for <c>NotFound</c> responses. Zero (the
    /// default) disables negative caching; only consumers exposing
    /// high-cardinality lookup endpoints should set it. Defends
    /// against cache-penetration DoS at the cost of making
    /// freshly-created records invisible for up to NegativeTtl.
    /// </summary>
    public TimeSpan NegativeTtl { get; init; }

    /// <summary>
    /// Procedure names (without service prefix) whose cache entries
    /// are flushed after a successful invocation of this method.
    /// Used on write methods. The interceptor composes the scoped
    /// invalidation prefix from the current write call's tenant
    /// scope and service path.
    /// </summary>
    public IReadOnlyList<string> Invalidates { get; init; } = Array.Empty<string>();

    /// <summary>True when the spec describes a cacheable read.</summary>
    public bool Cacheable => Ttl > TimeSpan.Zero;
}
