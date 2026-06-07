using System.Net.Http;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Inputs to <see cref="OidcDiscovery.DiscoverAsync"/>.
/// </summary>
/// <remarks>
/// Per cross-SDK RFC 0017, discovery is uncached at the SDK layer
/// and HTTPS-only. The HTTP client is consumer-owned so callers can
/// plug in their own handler chain (DPoP, OTel, mTLS, proxies)
/// without the OAuth package opining on transport.
/// </remarks>
public sealed class OidcDiscoveryConfig
{
    /// <summary>OIDC issuer URL. Must be HTTPS.</summary>
    public required Uri Issuer { get; init; }

    /// <summary>HTTP client used to fetch the discovery document.</summary>
    public required HttpClient HttpClient { get; init; }
}
