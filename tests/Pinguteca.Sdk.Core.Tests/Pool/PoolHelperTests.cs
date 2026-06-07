using System;
using System.Net.Http;
using Pinguteca.Sdk.Core.Pool;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Pool;

public sealed class PoolHelperTests
{
    [Test]
    public async Task CreateHandler_NoOptions_AppliesDefaults()
    {
        using SocketsHttpHandler handler = PoolHelper.CreateHandler();

        await Assert.That(handler.PooledConnectionLifetime).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(handler.PooledConnectionIdleTimeout).IsEqualTo(TimeSpan.FromMinutes(2));
        await Assert.That(handler.MaxConnectionsPerServer).IsEqualTo(int.MaxValue);
        await Assert.That(handler.EnableMultipleHttp2Connections).IsTrue();
    }

    [Test]
    public async Task CreateHandler_CustomOptions_AppliedToHandler()
    {
        var options = new PoolOptions
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(45),
            MaxConnectionsPerServer = 8,
            EnableMultipleHttp2Connections = false,
        };

        using SocketsHttpHandler handler = PoolHelper.CreateHandler(options);

        await Assert.That(handler.PooledConnectionLifetime).IsEqualTo(TimeSpan.FromMinutes(1));
        await Assert.That(handler.PooledConnectionIdleTimeout).IsEqualTo(TimeSpan.FromSeconds(45));
        await Assert.That(handler.MaxConnectionsPerServer).IsEqualTo(8);
        await Assert.That(handler.EnableMultipleHttp2Connections).IsFalse();
    }

    [Test]
    public async Task CreateHandler_ProducesIndependentHandlers()
    {
        // Each call returns a new instance; consumers manage disposal
        // (typically by transferring ownership to GrpcChannelOptions.HttpHandler
        // and disposing the channel).
        using SocketsHttpHandler first = PoolHelper.CreateHandler();
        using SocketsHttpHandler second = PoolHelper.CreateHandler();

        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }
}
