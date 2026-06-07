using System.Net;
using System.Net.Http;
using System.Text;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

/// <summary>
/// Discovery-driven flows must hit the mTLS-bound alias endpoint
/// (RFC 8705 section 5) when the consumer authenticates with a
/// client cert, and the regular endpoint otherwise.
/// </summary>
public sealed class MtlsRoutingTests
{
    private const string DiscoveryWithMtlsAliases = """
        {
          "issuer": "https://idp.example.com",
          "authorization_endpoint": "https://idp.example.com/oauth2/authorize",
          "token_endpoint": "https://idp.example.com/oauth2/token",
          "mtls_endpoint_aliases": {
            "token_endpoint": "https://mtls.idp.example.com/oauth2/token"
          }
        }
        """;

    private const string TokenJson = """{ "access_token": "atok", "token_type": "Bearer", "expires_in": 3600 }""";

    [Test]
    public async Task ClientCredentials_MtlsMode_RoutesToAliasTokenEndpoint()
    {
        Uri? tokenHit = null;
        using var http = new HttpClient(new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Ok(DiscoveryWithMtlsAliases);
            }
            tokenHit = req.RequestUri;
            return Ok(TokenJson);
        }));

        using var source = await ClientCredentialsTokenSource.FromIssuerAsync(new ClientCredentialsFromIssuerConfig
        {
            Issuer = new Uri("https://idp.example.com"),
            ClientId = "cid",
            ClientSecret = null,
            HttpClient = http,
            AuthMode = ClientAuthMode.Mtls,
        });
        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(tokenHit!.ToString())
            .IsEqualTo("https://mtls.idp.example.com/oauth2/token");
    }

    [Test]
    public async Task ClientCredentials_NonMtlsMode_IgnoresAliasEvenWhenPresent()
    {
        Uri? tokenHit = null;
        using var http = new HttpClient(new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Ok(DiscoveryWithMtlsAliases);
            }
            tokenHit = req.RequestUri;
            return Ok(TokenJson);
        }));

        using var source = await ClientCredentialsTokenSource.FromIssuerAsync(new ClientCredentialsFromIssuerConfig
        {
            Issuer = new Uri("https://idp.example.com"),
            ClientId = "cid",
            ClientSecret = "shh",
            HttpClient = http,
            AuthMode = ClientAuthMode.Basic,
        });
        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(tokenHit!.ToString())
            .IsEqualTo("https://idp.example.com/oauth2/token");
    }

    [Test]
    public async Task ClientCredentials_MtlsMode_NoAliasInDiscovery_FallsBackToTopLevel()
    {
        const string discoveryWithoutAliases = """
            {
              "issuer": "https://idp.example.com",
              "authorization_endpoint": "https://idp.example.com/oauth2/authorize",
              "token_endpoint": "https://idp.example.com/oauth2/token"
            }
            """;
        Uri? tokenHit = null;
        using var http = new HttpClient(new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Ok(discoveryWithoutAliases);
            }
            tokenHit = req.RequestUri;
            return Ok(TokenJson);
        }));

        using var source = await ClientCredentialsTokenSource.FromIssuerAsync(new ClientCredentialsFromIssuerConfig
        {
            Issuer = new Uri("https://idp.example.com"),
            ClientId = "cid",
            ClientSecret = null,
            HttpClient = http,
            AuthMode = ClientAuthMode.Mtls,
        });
        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(tokenHit!.ToString())
            .IsEqualTo("https://idp.example.com/oauth2/token");
    }

    [Test]
    public async Task AuthorizationCode_MtlsMode_RoutesTokenEndpointToAlias()
    {
        Uri? tokenHit = null;
        using var http = new HttpClient(new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Ok(DiscoveryWithMtlsAliases);
            }
            tokenHit = req.RequestUri;
            return Ok(TokenJson);
        }));

        var flow = await AuthorizationCodeFlow.FromIssuerAsync(new AuthorizationCodeFromIssuerConfig
        {
            Issuer = new Uri("https://idp.example.com"),
            ClientId = "cid",
            ClientSecret = null,
            RedirectUri = new Uri("https://app.example.com/callback"),
            HttpClient = http,
            AuthMode = ClientAuthMode.Mtls,
        });
        await flow.ExchangeAsync("code", "verifier-43-chars-aaaaaaaaaaaaaaaaaaaaaa");

        await Assert.That(tokenHit!.ToString())
            .IsEqualTo("https://mtls.idp.example.com/oauth2/token");
    }

    [Test]
    public async Task AuthorizationCode_MtlsMode_KeepsAuthorizationEndpointOnTopLevel()
    {
        using var http = new HttpClient(new StubHandler(req =>
            Ok(DiscoveryWithMtlsAliases)));

        var flow = await AuthorizationCodeFlow.FromIssuerAsync(new AuthorizationCodeFromIssuerConfig
        {
            Issuer = new Uri("https://idp.example.com"),
            ClientId = "cid",
            ClientSecret = null,
            RedirectUri = new Uri("https://app.example.com/callback"),
            HttpClient = http,
            AuthMode = ClientAuthMode.Mtls,
        });

        // The authorization endpoint is a browser redirect; mTLS only
        // gates the back-channel token request. The URL we build for
        // the user-agent should still point at the top-level host.
        Uri authUrl = flow.BuildAuthorizationUrl("state", PkcePair.Generate());
        await Assert.That(authUrl.GetLeftPart(UriPartial.Path))
            .IsEqualTo("https://idp.example.com/oauth2/authorize");
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
