using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class ClientCredentialsTokenSourceTests
{
    private static readonly Uri TokenEndpoint = new("https://idp.example.com/oauth2/token");

    private static ClientCredentialsConfig BuildConfig(
        HttpClient http,
        ClientAuthMode mode = ClientAuthMode.Basic,
        string? clientSecret = "shh",
        IReadOnlyList<string>? scopes = null,
        IReadOnlyDictionary<string, string>? additionalParameters = null) =>
        new()
        {
            TokenEndpoint = TokenEndpoint,
            ClientId = "cid",
            ClientSecret = clientSecret,
            Scopes = scopes,
            AdditionalParameters = additionalParameters,
            HttpClient = http,
            AuthMode = mode,
            RefreshSkew = TimeSpan.FromSeconds(30),
        };

    [Test]
    public async Task GetTokenAsync_HitsTokenEndpointAndReturnsAccessToken()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            capturedRequest = req;
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Ok("""{ "access_token": "atok", "token_type": "Bearer", "expires_in": 3600 }""");
        }));
        using var source = new ClientCredentialsTokenSource(BuildConfig(http));

        string token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("atok");
        await Assert.That(capturedRequest!.Method.Method).IsEqualTo("POST");
        await Assert.That(capturedRequest.RequestUri!.ToString()).IsEqualTo(TokenEndpoint.ToString());
        await Assert.That(capturedBody!).Contains("grant_type=client_credentials");
        await Assert.That(capturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Basic");
    }

    [Test]
    public async Task GetTokenAsync_FreshToken_ServesFromCache()
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Ok("""{ "access_token": "atok", "token_type": "Bearer", "expires_in": 3600 }""");
        }));
        using var source = new ClientCredentialsTokenSource(BuildConfig(http));

        await source.GetTokenAsync(CancellationToken.None);
        await source.GetTokenAsync(CancellationToken.None);
        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task GetTokenAsync_Expired_Refetches()
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Ok($$"""{ "access_token": "atok{{calls}}", "token_type": "Bearer", "expires_in": 60 }""");
        }));
        var clock = new FrozenClock(DateTimeOffset.UtcNow);
        using var source = new ClientCredentialsTokenSource(BuildConfig(http), clock.Now);

        await Assert.That(await source.GetTokenAsync(CancellationToken.None)).IsEqualTo("atok1");
        clock.Advance(TimeSpan.FromSeconds(45));
        await Assert.That(await source.GetTokenAsync(CancellationToken.None)).IsEqualTo("atok2");
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task GetTokenAsync_ConcurrentCallers_FetchOnce()
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(async _ =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(20);
            return Ok("""{ "access_token": "atok", "token_type": "Bearer", "expires_in": 3600 }""");
        }));
        using var source = new ClientCredentialsTokenSource(BuildConfig(http));

        var fan = new[]
        {
            source.GetTokenAsync(CancellationToken.None).AsTask(),
            source.GetTokenAsync(CancellationToken.None).AsTask(),
            source.GetTokenAsync(CancellationToken.None).AsTask(),
            source.GetTokenAsync(CancellationToken.None).AsTask(),
        };
        await Task.WhenAll(fan);

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Invalidate_ForcesRefetchOnNextCall()
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Ok("""{ "access_token": "atok", "token_type": "Bearer", "expires_in": 3600 }""");
        }));
        using var source = new ClientCredentialsTokenSource(BuildConfig(http));

        await source.GetTokenAsync(CancellationToken.None);
        source.Invalidate();
        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task GetTokenAsync_FormPostMode_PutsSecretInBody()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            capturedRequest = req;
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Ok("""{ "access_token": "atok", "token_type": "Bearer" }""");
        }));
        using var source = new ClientCredentialsTokenSource(BuildConfig(http, ClientAuthMode.FormPost));

        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(capturedRequest!.Headers.Authorization).IsNull();
        await Assert.That(capturedBody!).Contains("client_id=cid");
        await Assert.That(capturedBody!).Contains("client_secret=shh");
    }

    [Test]
    public async Task GetTokenAsync_NoneMode_NoSecretInBody()
    {
        string? capturedBody = null;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Ok("""{ "access_token": "atok", "token_type": "Bearer" }""");
        }));
        using var source = new ClientCredentialsTokenSource(BuildConfig(http, ClientAuthMode.None, clientSecret: null));

        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(capturedBody!).Contains("client_id=cid");
        await Assert.That(capturedBody!).DoesNotContain("client_secret");
    }

    [Test]
    public async Task GetTokenAsync_IncludesScopesAndAdditionalParameters()
    {
        string? capturedBody = null;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Ok("""{ "access_token": "atok", "token_type": "Bearer" }""");
        }));
        using var source = new ClientCredentialsTokenSource(BuildConfig(http,
            scopes: new[] { "api:read", "api:write" },
            additionalParameters: new Dictionary<string, string> { ["audience"] = "https://api.example" }));

        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(capturedBody!).Contains("scope=api%3Aread+api%3Awrite");
        await Assert.That(capturedBody!).Contains("audience=https%3A%2F%2Fapi.example");
    }

    [Test]
    public async Task GetTokenAsync_TokenEndpointError_ThrowsOAuthException()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{ "error": "invalid_client", "error_description": "bad secret" }""",
                    Encoding.UTF8, "application/json"),
            }));
        using var source = new ClientCredentialsTokenSource(BuildConfig(http));

        var ex = await Assert.That(async () => await source.GetTokenAsync(CancellationToken.None))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidClient);
        await Assert.That(ex.HttpStatusCode).IsEqualTo(401);
    }

    [Test]
    public async Task Constructor_BasicWithoutSecret_Throws()
    {
        using var http = new HttpClient(new StubHandler((Func<HttpRequestMessage, HttpResponseMessage>)(_ =>
            new HttpResponseMessage(HttpStatusCode.NotImplemented))));

        await Assert.That(() => new ClientCredentialsTokenSource(BuildConfig(http, ClientAuthMode.Basic, clientSecret: null)))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Constructor_NoneWithSecret_Throws()
    {
        using var http = new HttpClient(new StubHandler((Func<HttpRequestMessage, HttpResponseMessage>)(_ =>
            new HttpResponseMessage(HttpStatusCode.NotImplemented))));

        await Assert.That(() => new ClientCredentialsTokenSource(BuildConfig(http, ClientAuthMode.None, clientSecret: "leaked")))
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
        const string tokenJson = """{ "access_token": "atok", "token_type": "Bearer", "expires_in": 3600 }""";
        Uri? lastHit = null;
        using var http = new HttpClient(new StubHandler(req =>
        {
            lastHit = req.RequestUri;
            return Ok(req.RequestUri!.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal)
                ? discoveryJson
                : tokenJson);
        }));

        using var source = await ClientCredentialsTokenSource.FromIssuerAsync(new ClientCredentialsFromIssuerConfig
        {
            Issuer = new Uri("https://idp.example.com"),
            ClientId = "cid",
            ClientSecret = "shh",
            HttpClient = http,
        });

        string token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("atok");
        await Assert.That(lastHit!.ToString()).IsEqualTo("https://idp.example.com/oauth2/token");
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class FrozenClock
    {
        private DateTimeOffset _now;
        public FrozenClock(DateTimeOffset start) => _now = start;
        public DateTimeOffset Now() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
