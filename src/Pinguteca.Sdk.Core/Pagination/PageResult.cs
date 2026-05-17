using System.Collections.Generic;

namespace Pinguteca.Sdk.Core.Pagination;

/// <summary>
/// One page returned from <see cref="FetchPageAsync{T}"/>. An empty
/// <see cref="NextPageToken"/> signals the iterator that no further
/// pages exist; pagination terminates after yielding this page's
/// <see cref="Items"/>.
/// </summary>
public sealed record PageResult<T>(IReadOnlyList<T> Items, string NextPageToken);
