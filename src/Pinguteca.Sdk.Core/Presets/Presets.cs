using System;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Auth;
using Pinguteca.Sdk.Core.Breaker;
using Pinguteca.Sdk.Core.Idempotency;
using Pinguteca.Sdk.Core.Otel;
using Pinguteca.Sdk.Core.Retry;

namespace Pinguteca.Sdk.Core.Presets;

/// <summary>
/// Canned interceptor compositions matching RFC 0008.
///
/// <see cref="Standalone"/> wires the full resilience stack for
/// consumers running outside a service mesh (mobile, CLI, edge,
/// external services). <see cref="Mesh"/> wires only what the
/// mesh cannot do for the SDK (OTel client-side spans, idempotency
/// key generation, auth) so wiring breaker and retry on top of an
/// Istio/Linkerd/Cilium data plane does not multiply attempts or
/// double-trip circuits.
///
/// Both presets return interceptors in outermost-to-innermost
/// order so callers can pass them straight into
/// <c>CallInvoker.Intercept(...)</c>.
/// </summary>
public static class Presets
{
    public static Interceptor[] Standalone(PresetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return
        [
            new OtelInterceptor(options.Otel),
            new BreakerInterceptor(options.Breaker),
            new IdempotencyInterceptor(options.Idempotency),
            new RetryInterceptor(options.Retry),
            new AuthInterceptor(options.Auth),
        ];
    }

    public static Interceptor[] Mesh(PresetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return
        [
            new OtelInterceptor(options.Otel),
            new IdempotencyInterceptor(options.Idempotency),
            new AuthInterceptor(options.Auth),
        ];
    }
}
