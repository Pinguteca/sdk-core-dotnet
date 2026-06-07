using Aspire.Hosting.Testing;
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.IntegrationTests.Contract.V1;

namespace Pinguteca.Sdk.Core.IntegrationTests;

/// <summary>
/// End-to-end smoke test for the FauxRPC-backed harness. Boots the
/// Aspire AppHost, waits for the FauxRPC container to become healthy,
/// and exercises a single unary RPC against the descriptor pinned in
/// <c>proto/contract.binpb</c>. Streaming coverage and interceptor
/// assertions land in follow-up PRs against this same harness.
/// </summary>
public sealed class HarnessSmokeTests
{
    [Test]
    public async Task FauxRpc_UnaryEcho_RoundTrips()
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
        var client = new Harness.HarnessClient(channel);

        var reply = await client.EchoAsync(new EchoRequest { Message = "ping" });

        // FauxRPC synthesises field values from the descriptor; the
        // contract only guarantees a non-null reply payload.
        await Assert.That(reply).IsNotNull();
        await Assert.That(reply.Message).IsNotNull();
    }
}
