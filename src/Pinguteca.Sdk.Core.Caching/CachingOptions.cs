using System;
using System.Collections.Generic;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Pinguteca.Sdk.Core.Caching;

/// <summary>
/// Knobs for <see cref="CachingInterceptor"/>. Per RFC 0015,
/// caching is off unless <see cref="Store"/> and
/// <see cref="KeyScope"/> are both wired; missing either defers to
/// the default-deny passthrough.
/// </summary>
public sealed class CachingOptions
{
    /// <summary>
    /// Pluggable cache backend. When null the interceptor passes
    /// every call through.
    /// </summary>
    public ICache? Store { get; init; }

    /// <summary>
    /// Returns the tenant identifier for the supplied call context.
    /// Return empty string for explicit single-tenant mode.
    /// Required by default-deny: when null the interceptor passes
    /// every call through without caching.
    /// </summary>
    public Func<CallContext, string>? KeyScope { get; init; }

    /// <summary>
    /// Maps fully-qualified procedure paths
    /// (<c>/service.v1.Svc/Method</c>) to their cache specs.
    /// Methods absent from the map pass through uncached.
    /// </summary>
    public IReadOnlyDictionary<string, CacheSpec> MethodConfig { get; init; } =
        new Dictionary<string, CacheSpec>();

    /// <summary>
    /// Optional logger receiving one structured record per cache
    /// outcome (hit, miss, swr-hit, negative-hit, bypass).
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Overrides the clock for tests. Production code leaves this
    /// null so the interceptor uses <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    public Func<DateTimeOffset>? Now { get; init; }
}

/// <summary>
/// Minimal context handed to <see cref="CachingOptions.KeyScope"/>.
/// Lets the hook read call metadata without taking a dependency on
/// the full generic interceptor context.
/// </summary>
public sealed record CallContext(string Procedure, Metadata Headers);
