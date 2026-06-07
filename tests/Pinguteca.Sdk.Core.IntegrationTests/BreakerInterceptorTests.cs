using Aspire.Hosting.Testing;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.Breaker;
using Pinguteca.Sdk.Core.IntegrationTests.Contract.V1;
using Pinguteca.Sdk.Core.IntegrationTests.Fixtures;

namespace Pinguteca.Sdk.Core.IntegrationTests;

/// <summary>
/// Integration coverage for <see cref="BreakerInterceptor"/>. Drives
/// FauxRPC to return Unavailable on every Echo call, saturates the
/// rolling window, then asserts the next call short-circuits with
/// the breaker's distinctive "circuit breaker open" status detail
/// rather than another server-side failure.
/// </summary>
public sealed class BreakerInterceptorTests
{
    private const string EchoTarget = "pinguteca.sdk.core.integration.v1.Harness/Echo";

    [Test]
    public async Task TripsAfterMinSamplesFailures_ThenShortCircuits()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Pinguteca_Sdk_Core_IntegrationTests_AppHost>();
        await using var app = await builder.BuildAsync();
        await app.StartAsync();
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("fauxrpc")
            .WaitAsync(TimeSpan.FromSeconds(60));

        var endpoint = app.GetEndpoint("fauxrpc", "rpc");
        await FauxRpcStubs.AddErrorStubAsync(
            endpoint,
            EchoTarget,
            errorCode: "ERROR_CODE_UNAVAILABLE",
            message: "test injected");

        using var channel = GrpcChannel.ForAddress(endpoint);
        var breaker = new BreakerInterceptor(new BreakerOptions
        {
            FailureRateThreshold = 0.5,
            MinSamples = 3,
            WindowDuration = TimeSpan.FromSeconds(30),
            OpenDuration = TimeSpan.FromSeconds(2),
        });
        var client = new Harness.HarnessClient(channel.Intercept(breaker));

        // Three real network failures saturate the rolling window
        // and trip the breaker. Each surfaces as Unavailable from
        // FauxRPC and is recorded by ObserveResponseAsync.
        for (var i = 0; i < 3; i++)
        {
            await Assert.That(async () =>
                    await client.EchoAsync(new EchoRequest { Message = "ping" }))
                .ThrowsExactly<RpcException>();
        }

        // The fourth call must NOT reach FauxRPC. The breaker
        // returns its own RpcException with a distinctive status
        // detail and a retry-after trailer.
        var rejected = await Assert.That(async () =>
                await client.EchoAsync(new EchoRequest { Message = "ping" }))
            .ThrowsExactly<RpcException>();

        await Assert.That(rejected!.Status.StatusCode).IsEqualTo(StatusCode.Unavailable);
        await Assert.That(rejected.Status.Detail).IsEqualTo("circuit breaker open");
    }
}
