using System.Threading;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Core.Caching;

/// <summary>
/// Result of a cache lookup. <see cref="Found"/> is false on a
/// clean miss; otherwise <see cref="Entry"/> holds the cached
/// response.
/// </summary>
public readonly record struct CacheLookup(Entry? Entry, bool Found);

/// <summary>
/// Pluggable cache backend. In-memory and (future) distributed
/// implementations live alongside this package; consumers may
/// implement their own.
/// </summary>
public interface ICache
{
    /// <summary>
    /// Returns the cached entry for <paramref name="key"/> when
    /// present and within the SWR hard deadline. Past-deadline
    /// entries are evicted inline by the implementation.
    /// </summary>
    ValueTask<CacheLookup> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or refreshes the entry for <paramref name="key"/>.
    /// </summary>
    ValueTask SetAsync(string key, Entry entry, CancellationToken cancellationToken);

    /// <summary>Removes the entry for <paramref name="key"/>. No-op when absent.</summary>
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every entry whose key contains <paramref name="prefix"/>
    /// as a substring. The interceptor invokes this with a scoped
    /// prefix during write-triggered invalidation.
    /// </summary>
    ValueTask DeleteMatchingAsync(string prefix, CancellationToken cancellationToken);
}
