using System;
using Pinguteca.Sdk.Core.Auth;
using Pinguteca.Sdk.Core.Breaker;
using Pinguteca.Sdk.Core.Idempotency;
using Pinguteca.Sdk.Core.Otel;
using Pinguteca.Sdk.Core.Presets;
using Pinguteca.Sdk.Core.Retry;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Presets;

public sealed class PresetsTests
{
    private static PresetOptions NewOptions() => new()
    {
        Auth = new AuthOptions { Source = new StaticBearerTokenSource("t") },
    };

    [Test]
    public async Task StandaloneWiresFiveInterceptorsInOrder()
    {
        var chain = Sdk.Core.Presets.Presets.Standalone(NewOptions());

        await Assert.That(chain.Length).IsEqualTo(5);
        await Assert.That(chain[0]).IsTypeOf<OtelInterceptor>();
        await Assert.That(chain[1]).IsTypeOf<BreakerInterceptor>();
        await Assert.That(chain[2]).IsTypeOf<IdempotencyInterceptor>();
        await Assert.That(chain[3]).IsTypeOf<RetryInterceptor>();
        await Assert.That(chain[4]).IsTypeOf<AuthInterceptor>();
    }

    [Test]
    public async Task MeshSkipsBreakerAndRetry()
    {
        var chain = Sdk.Core.Presets.Presets.Mesh(NewOptions());

        await Assert.That(chain.Length).IsEqualTo(3);
        await Assert.That(chain[0]).IsTypeOf<OtelInterceptor>();
        await Assert.That(chain[1]).IsTypeOf<IdempotencyInterceptor>();
        await Assert.That(chain[2]).IsTypeOf<AuthInterceptor>();
    }

    [Test]
    public void StandaloneRejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => Sdk.Core.Presets.Presets.Standalone(null!));
    }

    [Test]
    public void MeshRejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => Sdk.Core.Presets.Presets.Mesh(null!));
    }
}
