using Aspire.Hosting.Testing;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.IntegrationTests.Contract.V1;
using Pinguteca.Sdk.Core.Timeouts;

namespace Pinguteca.Sdk.Core.IntegrationTests;

/// <summary>
/// First interceptor-level integration test against the FauxRPC
/// harness. Wires <see cref="TimeoutInterceptor"/> onto a channel and
/// asserts that a deadline shorter than any realistic round-trip
/// surfaces as DeadlineExceeded under real network transport. Catches
/// drift the unit tests cannot see (e.g. interceptor chain wiring,
/// real CallOptions.Deadline propagation through the gRPC stack).
/// </summary>
public sealed class TimeoutInterceptorTests
{
    [Test]
    public async Task SubMillisecondDeadline_SurfacesDeadlineExceeded()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Pinguteca_Sdk_Core_IntegrationTests_AppHost>();
        await using var app = await builder.BuildAsync();
        await app.StartAsync();
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("fauxrpc")
            .WaitAsync(TimeSpan.FromSeconds(60));

        var endpoint = app.GetEndpoint("fauxrpc", "rpc");
        using var channel = GrpcChannel.ForAddress(endpoint);
        var interceptor = new TimeoutInterceptor(
            new TimeoutOptions { Default = TimeSpan.FromMilliseconds(1) });
        var invoker = channel.Intercept(interceptor);
        var client = new Harness.HarnessClient(invoker);

        var rpc = await Assert.That(async () =>
                await client.EchoAsync(new EchoRequest { Message = "ping" }))
            .ThrowsExactly<RpcException>();

        await Assert.That(rpc!.StatusCode).IsEqualTo(StatusCode.DeadlineExceeded);
    }
}
