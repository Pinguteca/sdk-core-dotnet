namespace Pinguteca.Sdk.Core.IntegrationTests;

/// <summary>
/// Placeholder for the FauxRPC harness. The integration project exists so
/// the slnx and package-reference wiring compile end-to-end before the
/// Aspire AppHost, sample .proto, and FauxRPC container resource land.
///
/// The Aspire AppHost project required to drive a real
/// <c>DistributedApplicationTestingBuilder</c> arrives in PR-B together
/// with the descriptor pipeline; this test is skipped until then.
/// </summary>
public sealed class HarnessSmokeTests
{
    [Test]
    [Skip("Pending PR-B: AppHost project, FauxRPC container, proto descriptor")]
    public Task PlaceholderForFauxRpcIntegration() => Task.CompletedTask;
}
