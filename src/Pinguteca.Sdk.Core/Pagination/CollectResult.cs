using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Pinguteca.Sdk.Core.Pagination;

/// <summary>
/// Outcome of <see cref="Paginator.CollectAsync{T}(FetchPageAsync{T}, System.Threading.CancellationToken)"/>.
/// <see cref="Items"/> holds whatever the iterator gathered before
/// it terminated; <see cref="Error"/> is null on full success and
/// non-null when iteration stopped early on a fetch failure or
/// cancellation.
///
/// Cross-SDK contract pinned in
/// <c>sdk-scaffold/docs/rfc/0009-pagination-api-shape.md</c>:
/// "Collect returns partial-on-error". All-or-nothing semantics are
/// trivial to layer on top via <see cref="EnsureSuccess"/>.
/// </summary>
public sealed record CollectResult<T>(IReadOnlyList<T> Items, Exception? Error)
{
    /// <summary>Convenience predicate for success.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>
    /// Re-throws <see cref="Error"/> preserving its original stack
    /// trace when this result represents a failure. No-op on success.
    /// </summary>
    public void EnsureSuccess()
    {
        if (Error is not null)
        {
            ExceptionDispatchInfo.Capture(Error).Throw();
        }
    }
}
