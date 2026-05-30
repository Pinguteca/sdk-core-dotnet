using System;
using System.Collections.Generic;
using Grpc.Core;

namespace Pinguteca.Sdk.Core.Caching;

/// <summary>
/// One cached unary response: the serialised body bytes, response
/// trailers worth replaying (notably ETag), the gRPC status, the
/// ETag for revalidation, and the bookkeeping for TTL plus
/// stale-while-revalidate.
/// </summary>
public sealed class Entry
{
    /// <summary>Serialised protobuf bytes of the cached response.</summary>
    public byte[] Body { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Response metadata captured at fetch time. Persisted so cache
    /// hits replay headers like <c>x-request-id</c> alongside the
    /// body. Binary metadata is filtered out at capture time.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } =
        Array.Empty<KeyValuePair<string, string>>();

    /// <summary>
    /// ETag from the server, when present. Sent as
    /// <c>If-None-Match</c> on the background refresh during SWR.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>Final gRPC status code from the cached call.</summary>
    public StatusCode Status { get; init; } = StatusCode.OK;

    /// <summary>UTC moment the entry was written to the cache.</summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>
    /// Cache-hit window. Past <c>Created + Ttl</c> the entry is
    /// either expired (no SWR) or stale (within SWR).
    /// </summary>
    public TimeSpan Ttl { get; init; }

    /// <summary>
    /// Stale-while-revalidate window beyond <see cref="Ttl"/>. Zero
    /// disables SWR.
    /// </summary>
    public TimeSpan Swr { get; init; }

    /// <summary>True when <paramref name="now"/> is past TTL.</summary>
    public bool Expired(DateTimeOffset now) => now > Created + Ttl;

    /// <summary>
    /// True when <paramref name="now"/> is in the SWR window past
    /// TTL but before the hard SWR deadline.
    /// </summary>
    public bool Stale(DateTimeOffset now)
    {
        if (Swr <= TimeSpan.Zero) return false;
        return Expired(now) && now <= Created + Ttl + Swr;
    }
}
