using System;

namespace Pinguteca.Sdk.Core.Idempotency;

/// <summary>
/// Knobs for <see cref="IdempotencyInterceptor"/>. Defaults match the
/// cross-SDK contract: a single Idempotency-Key header carrying a
/// UUIDv7 generated from the CSPRNG.
/// </summary>
public sealed class IdempotencyOptions
{
    public string HeaderName { get; init; } = "Idempotency-Key";

    /// <summary>
    /// Hook for test-time deterministic keys. Production code leaves
    /// this null and the interceptor uses <see cref="Guid.CreateVersion7()"/>.
    /// </summary>
    public Func<string>? KeyFactory { get; init; }
}
