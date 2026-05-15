using Pinguteca.Sdk.Core.Auth;
using Pinguteca.Sdk.Core.Breaker;
using Pinguteca.Sdk.Core.Idempotency;
using Pinguteca.Sdk.Core.Otel;
using Pinguteca.Sdk.Core.Retry;

namespace Pinguteca.Sdk.Core.Presets;

/// <summary>
/// Bundles the per-interceptor options consumed by
/// <see cref="Presets"/>. <see cref="Auth"/> is required because
/// both presets include the auth interceptor (innermost) and
/// every credential source needs explicit configuration. The
/// other fields fall back to per-interceptor defaults when null.
/// </summary>
public sealed class PresetOptions
{
    public required AuthOptions Auth { get; init; }
    public OtelOptions? Otel { get; init; }
    public BreakerOptions? Breaker { get; init; }
    public IdempotencyOptions? Idempotency { get; init; }
    public RetryOptions? Retry { get; init; }
}
