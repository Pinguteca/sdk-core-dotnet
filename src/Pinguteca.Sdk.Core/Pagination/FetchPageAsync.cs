using System.Threading;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Core.Pagination;

/// <summary>
/// Fetches one page of items. Receives a page token (empty on the
/// first call) and returns the items plus the token for the next
/// call (empty when there are no more pages).
///
/// Closures over the consumer's generated client are simpler than
/// wrapping the client in a one-method interface; the delegate type
/// keeps the caller's call site idiomatic.
/// </summary>
public delegate Task<PageResult<T>> FetchPageAsync<T>(
    string pageToken,
    CancellationToken cancellationToken);
