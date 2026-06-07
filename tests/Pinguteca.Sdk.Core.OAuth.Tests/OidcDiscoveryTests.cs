using System.Net;
using System.Net.Http;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class OidcDiscoveryTests
{
    private static readonly Uri Issuer = new("https://idp.example.com");

    [Test]
    public async Task DiscoverAsync_HappyPath_ReturnsMetadata()
    {
        const string json = """
            {
              "issuer": "https://idp.example.com",
              "authorization_endpoint": "https://idp.example.com/oauth2/authorize",
              "token_endpoint": "https://idp.example.com/oauth2/token",
              "userinfo_endpoint": "https://idp.example.com/userinfo",
              "jwks_uri": "https://idp.example.com/.well-known/jwks.json",
              "scopes_supported": ["openid", "profile", "email"],
              "grant_types_supported": ["authorization_code", "refresh_token", "client_credentials"],
              "token_endpoint_auth_methods_supported": ["client_secret_basic", "client_secret_post"],
              "code_challenge_methods_supported": ["S256"]
            }
            """;
        using var http = new HttpClient(new StubHandler(_ => Ok(json)));
        var config = new OidcDiscoveryConfig { Issuer = Issuer, HttpClient = http };

        var metadata = await OidcDiscovery.DiscoverAsync(config);

        await Assert.That(metadata.Issuer).IsEqualTo("https://idp.example.com");
        await Assert.That(metadata.TokenEndpoint.ToString()).IsEqualTo("https://idp.example.com/oauth2/token");
        await Assert.That(metadata.AuthorizationEndpoint.ToString()).IsEqualTo("https://idp.example.com/oauth2/authorize");
        await Assert.That(metadata.ScopesSupported).Contains("openid");
        await Assert.That(metadata.CodeChallengeMethodsSupported).Contains("S256");
        await Assert.That(metadata.MtlsEndpointAliases).IsNull();
    }

    [Test]
    public async Task DiscoverAsync_HitsWellKnownEndpoint()
    {
        Uri? requested = null;
        const string json = """
            {
              "issuer": "https://idp.example.com",
              "authorization_endpoint": "https://idp.example.com/a",
              "token_endpoint": "https://idp.example.com/t"
            }
            """;
        using var http = new HttpClient(new StubHandler(req => { requested = req.RequestUri; return Ok(json); }));
        var config = new OidcDiscoveryConfig { Issuer = Issuer, HttpClient = http };

        await OidcDiscovery.DiscoverAsync(config);

        await Assert.That(requested!.ToString())
            .IsEqualTo("https://idp.example.com/.well-known/openid-configuration");
    }

    [Test]
    public async Task DiscoverAsync_NormalizesTrailingSlashOnIssuer()
    {
        Uri? requested = null;
        const string json = """
            {
              "issuer": "https://idp.example.com/",
              "authorization_endpoint": "https://idp.example.com/a",
              "token_endpoint": "https://idp.example.com/t"
            }
            """;
        using var http = new HttpClient(new StubHandler(req => { requested = req.RequestUri; return Ok(json); }));
        var config = new OidcDiscoveryConfig
        {
            Issuer = new Uri("https://idp.example.com/"),
            HttpClient = http,
        };

        await OidcDiscovery.DiscoverAsync(config);

        // No double slash before .well-known.
        await Assert.That(requested!.ToString())
            .IsEqualTo("https://idp.example.com/.well-known/openid-configuration");
    }

    [Test]
    public async Task DiscoverAsync_RejectsHttpScheme()
    {
        using var http = new HttpClient(new StubHandler(_ => Ok("{}")));
        var config = new OidcDiscoveryConfig
        {
            Issuer = new Uri("http://idp.example.com"),
            HttpClient = http,
        };

        var ex = await Assert.That(async () => await OidcDiscovery.DiscoverAsync(config))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidIssuer);
    }

    [Test]
    public async Task DiscoverAsync_NonSuccessStatus_ThrowsInvalidIssuer()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("nope"),
            }));
        var config = new OidcDiscoveryConfig { Issuer = Issuer, HttpClient = http };

        var ex = await Assert.That(async () => await OidcDiscovery.DiscoverAsync(config))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidIssuer);
        await Assert.That(ex.HttpStatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task DiscoverAsync_MalformedJson_ThrowsInvalidResponse()
    {
        using var http = new HttpClient(new StubHandler(_ => Ok("{not json")));
        var config = new OidcDiscoveryConfig { Issuer = Issuer, HttpClient = http };

        var ex = await Assert.That(async () => await OidcDiscovery.DiscoverAsync(config))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidResponse);
    }

    [Test]
    public async Task DiscoverAsync_MissingTokenEndpoint_ThrowsInvalidResponse()
    {
        const string json = """
            {
              "issuer": "https://idp.example.com",
              "authorization_endpoint": "https://idp.example.com/a"
            }
            """;
        using var http = new HttpClient(new StubHandler(_ => Ok(json)));
        var config = new OidcDiscoveryConfig { Issuer = Issuer, HttpClient = http };

        var ex = await Assert.That(async () => await OidcDiscovery.DiscoverAsync(config))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidResponse);
    }

    [Test]
    public async Task DiscoverAsync_IssuerMismatch_ThrowsInvalidIssuer()
    {
        const string json = """
            {
              "issuer": "https://attacker.example.com",
              "authorization_endpoint": "https://attacker.example.com/a",
              "token_endpoint": "https://attacker.example.com/t"
            }
            """;
        using var http = new HttpClient(new StubHandler(_ => Ok(json)));
        var config = new OidcDiscoveryConfig { Issuer = Issuer, HttpClient = http };

        var ex = await Assert.That(async () => await OidcDiscovery.DiscoverAsync(config))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidIssuer);
    }

    [Test]
    public async Task DiscoverAsync_MtlsEndpointAliases_AreParsed()
    {
        const string json = """
            {
              "issuer": "https://idp.example.com",
              "authorization_endpoint": "https://idp.example.com/a",
              "token_endpoint": "https://idp.example.com/t",
              "mtls_endpoint_aliases": {
                "token_endpoint": "https://mtls.idp.example.com/t",
                "revocation_endpoint": "https://mtls.idp.example.com/r"
              }
            }
            """;
        using var http = new HttpClient(new StubHandler(_ => Ok(json)));
        var config = new OidcDiscoveryConfig { Issuer = Issuer, HttpClient = http };

        var metadata = await OidcDiscovery.DiscoverAsync(config);

        await Assert.That(metadata.MtlsEndpointAliases).IsNotNull();
        await Assert.That(metadata.MtlsEndpointAliases!.TokenEndpoint!.ToString())
            .IsEqualTo("https://mtls.idp.example.com/t");
        await Assert.That(metadata.MtlsEndpointAliases!.RevocationEndpoint!.ToString())
            .IsEqualTo("https://mtls.idp.example.com/r");
        await Assert.That(metadata.MtlsEndpointAliases!.IntrospectionEndpoint).IsNull();
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => Task.FromResult(_respond(request));
}
