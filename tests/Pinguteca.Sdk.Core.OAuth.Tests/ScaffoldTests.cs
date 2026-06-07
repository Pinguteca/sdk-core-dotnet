namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class ScaffoldTests
{
    [Test]
    public async Task Package_marker_points_at_rfc_0017()
    {
        // Sanity test for the scaffold release. Replaced by real
        // flow tests as each grant lands per the RFC 0017 contract.
        await Assert.That(typeof(PackageInfo).Assembly.GetName().Name)
            .IsEqualTo("Pinguteca.Sdk.Core.OAuth");
    }
}
