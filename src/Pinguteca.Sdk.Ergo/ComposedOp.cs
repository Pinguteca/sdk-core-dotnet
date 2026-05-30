using System;
using System.Globalization;
using System.Threading.Tasks;
using Grpc.Core;

namespace Pinguteca.Sdk.Ergo;

/// <summary>
/// Orchestrates a multi-RPC operation under one Layer 1.5 entry
/// point per RFC 0016. Each call to <see cref="RunAsync"/> derives
/// a fresh idempotency key as <c>{Id}/{leg}</c> and threads the
/// correlation id into the gRPC metadata. Distinct ComposedOp
/// instances get distinct ids (concurrent invocations do not
/// collide); distinct legs of one instance get distinct keys
/// (independent retryability).
///
/// Pair with <see cref="Pinguteca.Sdk.Core.Idempotency.IdempotencyInterceptor"/>
/// on the channel so the Layer 2 chain reads the per-leg keys.
/// </summary>
public sealed class ComposedOp
{
    /// <summary>
    /// Metadata header name carrying the per-leg idempotency key.
    /// Matches the cross-SDK convention from RFC 0008.
    /// </summary>
    public const string IdempotencyKeyHeader = "idempotency-key";

    /// <summary>
    /// Metadata header name carrying the correlation id. Threaded
    /// through every leg of the composed op.
    /// </summary>
    public const string CorrelationIdHeader = "correlation-id";

    /// <summary>
    /// Unique identifier for this composed-op invocation. Generated
    /// at construction time; never reused.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Correlation id threaded through every leg. Inherited from
    /// the constructor argument when supplied; freshly generated
    /// otherwise.
    /// </summary>
    public string Correlation { get; }

    private int _leg;

    private ComposedOp(string id, string correlation)
    {
        Id = id;
        Correlation = correlation;
    }

    /// <summary>
    /// Constructs a new ComposedOp with a freshly generated id and
    /// correlation id.
    /// </summary>
    public static ComposedOp New()
    {
        return new ComposedOp(IdGenerator.NewId(), IdGenerator.NewId());
    }

    /// <summary>
    /// Constructs a new ComposedOp inheriting <paramref name="correlation"/>
    /// for the correlation id. Passing an empty or null value generates
    /// a fresh correlation id.
    /// </summary>
    public static ComposedOp Continue(string? correlation)
    {
        var corr = string.IsNullOrEmpty(correlation) ? IdGenerator.NewId() : correlation;
        return new ComposedOp(IdGenerator.NewId(), corr);
    }

    /// <summary>
    /// Runs <paramref name="call"/> as the next leg of this composed
    /// op. The supplied callback receives a <see cref="CallOptions"/>
    /// derived from <paramref name="baseOptions"/> with the per-leg
    /// idempotency key and correlation id attached as metadata
    /// headers.
    /// </summary>
    public async Task<T> RunAsync<T>(CallOptions baseOptions, Func<CallOptions, Task<T>> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var legOptions = ApplyHeaders(baseOptions, _leg);
        _leg++;
        return await call(legOptions).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a <see cref="CallOptions"/> derived from
    /// <paramref name="baseOptions"/> with the per-leg metadata
    /// headers applied. Use this overload when the L1 client method
    /// is synchronous or returns a non-Task type that needs custom
    /// awaiting.
    /// </summary>
    public CallOptions NextLegOptions(CallOptions baseOptions)
    {
        var legOptions = ApplyHeaders(baseOptions, _leg);
        _leg++;
        return legOptions;
    }

    private CallOptions ApplyHeaders(CallOptions baseOptions, int legIndex)
    {
        var headers = baseOptions.Headers ?? [];
        var newHeaders = new Metadata();
        foreach (var entry in headers)
        {
            newHeaders.Add(entry);
        }
        newHeaders.Add(IdempotencyKeyHeader, BuildKey(legIndex));
        newHeaders.Add(CorrelationIdHeader, Correlation);
        return baseOptions.WithHeaders(newHeaders);
    }

    private string BuildKey(int legIndex)
    {
        return string.Concat(Id, "/", legIndex.ToString(CultureInfo.InvariantCulture));
    }
}
