using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class AuthorizationCodeFlowTests
{
    private static AuthorizationCodeConfig BuildConfig(
        HttpClient http,
        ClientAuthMode mode = ClientAuthMode.Basic,
        string? clientSecret = "shh",
        IReadOnlyList<string>? scopes = null) =>
        new()
        {
            ClientId = "cid",
            ClientSecret = clientSecret,
            AuthorizationEndpoint = new Uri("https://idp.example.com/oauth2/authorize"),
            TokenEndpoint = new Uri("https://idp.example.com/oauth2/token"),
            RedirectUri = new Uri("https://app.example.com/callback"),
            Scopes = scopes,
            HttpClient = http,
            AuthMode = mode,
        };

    [Test]
    public async Task BuildAuthorizationUrl_EmitsRequiredParams()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented)));
        var flow = new AuthorizationCodeFlow(BuildConfig(http, scopes: new[] { "openid", "profile" }));
        var pkce = PkcePair.FromVerifier("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        Uri url = flow.BuildAuthorizationUrl("state-xyz", pkce);

        var q = HttpUtility.ParseQueryString(url.Query);
        await Assert.That(url.GetLeftPart(UriPartial.Path))
            .IsEqualTo("https://idp.example.com/oauth2/authorize");
        await Assert.That(q["response_type"]).IsEqualTo("code");
        await Assert.That(q["client_id"]).IsEqualTo("cid");
        await Assert.That(q["redirect_uri"]).IsEqualTo("https://app.example.com/callback");
        await Assert.That(q["scope"]).IsEqualTo("openid profile");
        await Assert.That(q["state"]).IsEqualTo("state-xyz");
        await Assert.That(q["code_challenge"]).IsEqualTo(pkce.Challenge);
        await Assert.That(q["code_challenge_method"]).IsEqualTo("S256");
    }

    [Test]
    public async Task BuildAuthorizationUrl_PreservesExistingQueryParams()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented)));
        // Some IdPs require an audience or tenant query parameter on
        // the authorization endpoint itself; the flow should not
        // clobber it.
        var config = new AuthorizationCodeConfig
        {
            ClientId = "cid",
            ClientSecret = "shh",
            AuthorizationEndpoint = new Uri("https://idp.example.com/oauth2/authorize?tenant=acme"),
            TokenEndpoint = new Uri("https://idp.example.com/oauth2/token"),
            RedirectUri = new Uri("https://app.example.com/callback"),
            HttpClient = http,
        };
        var flow = new AuthorizationCodeFlow(config);

        Uri url = flow.BuildAuthorizationUrl("s", PkcePair.Generate());

        var q = HttpUtility.ParseQueryString(url.Query);
        await Assert.That(q["tenant"]).IsEqualTo("acme");
        await Assert.That(q["response_type"]).IsEqualTo("code");
    }

    [Test]
    public async Task BuildAuthorizationUrl_RejectsEmptyState()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented)));
        var flow = new AuthorizationCodeFlow(BuildConfig(http));

        await Assert.That(() => flow.BuildAuthorizationUrl(string.Empty, PkcePair.Generate()))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task BuildAuthorizationUrl_RejectsNullPkce()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented)));
        var flow = new AuthorizationCodeFlow(BuildConfig(http));

        await Assert.That(() => flow.BuildAuthorizationUrl("s", null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ExchangeAsync_SendsAuthorizationCodeForm()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            capturedRequest = req;
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Ok("""{ "access_token": "atok", "token_type": "Bearer", "expires_in": 3600 }""");
        }));
        var flow = new AuthorizationCodeFlow(BuildConfig(http));

        var resp = await flow.ExchangeAsync("authcode", "verifier-43-chars-aaaaaaaaaaaaaaaaaaaaaa");

        await Assert.That(resp.AccessToken).IsEqualTo("atok");
        await Assert.That(capturedRequest!.Method.Method).IsEqualTo("POST");
        await Assert.That(capturedRequest.RequestUri!.ToString()).IsEqualTo("https://idp.example.com/oauth2/token");
        await Assert.That(capturedBody!).Contains("grant_type=authorization_code");
        await Assert.That(capturedBody!).Contains("code=authcode");
        await Assert.That(capturedBody!).Contains("code_verifier=verifier-43-chars-aaaaaaaaaaaaaaaaaaaaaa");
        await Assert.That(capturedBody!).Contains("redirect_uri=https%3A%2F%2Fapp.example.com%2Fcallback");
        await Assert.That(capturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Basic");
    }

    [Test]
    public async Task ExchangeAsync_FormPostMode_PutsSecretInBody()
    {
        string? capturedBody = null;
        HttpRequestMessage? capturedRequest = null;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            capturedRequest = req;
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Ok("""{ "access_token": "atok", "token_type": "Bearer" }""");
        }));
        var flow = new AuthorizationCodeFlow(BuildConfig(http, ClientAuthMode.FormPost));

        await flow.ExchangeAsync("code", "verifier-43-chars-aaaaaaaaaaaaaaaaaaaaaa");

        await Assert.That(capturedRequest!.Headers.Authorization).IsNull();
        await Assert.That(capturedBody!).Contains("client_id=cid");
        await Assert.That(capturedBody!).Contains("client_secret=shh");
    }

    [Test]
    public async Task ExchangeAsync_NoneMode_NoSecretInBody()
    {
        string? capturedBody = null;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Ok("""{ "access_token": "atok", "token_type": "Bearer" }""");
        }));
        var flow = new AuthorizationCodeFlow(BuildConfig(http, ClientAuthMode.None, clientSecret: null));

        await flow.ExchangeAsync("code", "verifier-43-chars-aaaaaaaaaaaaaaaaaaaaaa");

        await Assert.That(capturedBody!).Contains("client_id=cid");
        await Assert.That(capturedBody!).DoesNotContain("client_secret");
    }

    [Test]
    public async Task ExchangeAsync_TokenEndpointError_ThrowsOAuthException()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{ "error": "invalid_grant", "error_description": "bad code" }""",
                    Encoding.UTF8, "application/json"),
            }));
        var flow = new AuthorizationCodeFlow(BuildConfig(http));

        var ex = await Assert.That(async () => await flow.ExchangeAsync("c", "verifier-43-chars-aaaaaaaaaaaaaaaaaaaaaa"))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidGrant);
        await Assert.That(ex.HttpStatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task RefreshAsync_SendsRefreshTokenForm()
    {
        string? capturedBody = null;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Ok("""{ "access_token": "atok2", "token_type": "Bearer", "expires_in": 1800, "refresh_token": "rtok2" }""");
        }));
        var flow = new AuthorizationCodeFlow(BuildConfig(http, scopes: new[] { "openid" }));

        var resp = await flow.RefreshAsync("rtok");

        await Assert.That(resp.AccessToken).IsEqualTo("atok2");
        await Assert.That(resp.RefreshToken).IsEqualTo("rtok2");
        await Assert.That(capturedBody!).Contains("grant_type=refresh_token");
        await Assert.That(capturedBody!).Contains("refresh_token=rtok");
        await Assert.That(capturedBody!).Contains("scope=openid");
    }

    [Test]
    public async Task Constructor_BasicWithoutSecret_Throws()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented)));

        await Assert.That(() => new AuthorizationCodeFlow(BuildConfig(http, ClientAuthMode.Basic, clientSecret: null)))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Constructor_NoneWithSecret_Throws()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented)));

        await Assert.That(() => new AuthorizationCodeFlow(BuildConfig(http, ClientAuthMode.None, clientSecret: "leaked")))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task FromIssuerAsync_RunsDiscoveryAndUsesEndpoints()
    {
        const string discoveryJson = """
            {
              "issuer": "https://idp.example.com",
              "authorization_endpoint": "https://idp.example.com/oauth2/authorize",
              "token_endpoint": "https://idp.example.com/oauth2/token"
            }
            """;
        const string tokenJson = """{ "access_token": "atok", "token_type": "Bearer" }""";

        using var http = new HttpClient(new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal)
                ? Ok(discoveryJson)
                : Ok(tokenJson)));

        var flow = await AuthorizationCodeFlow.FromIssuerAsync(new AuthorizationCodeFromIssuerConfig
        {
            Issuer = new Uri("https://idp.example.com"),
            ClientId = "cid",
            ClientSecret = "shh",
            RedirectUri = new Uri("https://app.example.com/callback"),
            HttpClient = http,
        });

        Uri url = flow.BuildAuthorizationUrl("state", PkcePair.Generate());
        await Assert.That(url.GetLeftPart(UriPartial.Path))
            .IsEqualTo("https://idp.example.com/oauth2/authorize");
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
