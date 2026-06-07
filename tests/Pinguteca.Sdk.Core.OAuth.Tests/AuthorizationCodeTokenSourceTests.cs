using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class AuthorizationCodeTokenSourceTests
{
    private static AuthorizationCodeFlow BuildFlow(HttpClient http) =>
        new(new AuthorizationCodeConfig
        {
            ClientId = "cid",
            ClientSecret = "shh",
            AuthorizationEndpoint = new Uri("https://idp.example.com/oauth2/authorize"),
            TokenEndpoint = new Uri("https://idp.example.com/oauth2/token"),
            RedirectUri = new Uri("https://app.example.com/callback"),
            HttpClient = http,
            RefreshSkew = TimeSpan.FromSeconds(30),
        });

    [Test]
    public async Task GetTokenAsync_FreshToken_ServesFromCache()
    {
        using var http = new HttpClient(new StubHandler((Func<HttpRequestMessage, HttpResponseMessage>)(_ =>
            throw new InvalidOperationException("Should not hit the token endpoint while fresh."))));
        var flow = BuildFlow(http);
        var clock = new FrozenClock(DateTimeOffset.UtcNow);
        var source = new AuthorizationCodeTokenSource(flow,
            new TokenResponse("atok", "Bearer", 3600, "rtok", null, null),
            clock.Now);

        string token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("atok");
    }

    [Test]
    public async Task GetTokenAsync_Expired_RefreshesAndRotates()
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Ok("""{ "access_token": "atok2", "token_type": "Bearer", "expires_in": 3600, "refresh_token": "rtok2" }""");
        }));
        var flow = BuildFlow(http);
        var clock = new FrozenClock(DateTimeOffset.UtcNow);
        var source = new AuthorizationCodeTokenSource(flow,
            new TokenResponse("atok", "Bearer", 60, "rtok", null, null),
            clock.Now);

        // Advance past the refresh skew (30s before the 60s expiry).
        clock.Advance(TimeSpan.FromSeconds(45));

        string token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("atok2");
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task GetTokenAsync_ConcurrentExpiredCallers_RefreshOnce()
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(async _ =>
        {
            Interlocked.Increment(ref calls);
            // Simulate IdP latency so concurrent callers race the gate.
            await Task.Delay(20);
            return Ok("""{ "access_token": "atok2", "token_type": "Bearer", "expires_in": 3600, "refresh_token": "rtok2" }""");
        }));
        var flow = BuildFlow(http);
        var clock = new FrozenClock(DateTimeOffset.UtcNow);
        var source = new AuthorizationCodeTokenSource(flow,
            new TokenResponse("atok", "Bearer", 60, "rtok", null, null),
            clock.Now);
        clock.Advance(TimeSpan.FromSeconds(45));

        var fan = new[]
        {
            source.GetTokenAsync(CancellationToken.None).AsTask(),
            source.GetTokenAsync(CancellationToken.None).AsTask(),
            source.GetTokenAsync(CancellationToken.None).AsTask(),
            source.GetTokenAsync(CancellationToken.None).AsTask(),
        };
        string[] results = await Task.WhenAll(fan);

        foreach (string r in results)
        {
            await Assert.That(r).IsEqualTo("atok2");
        }
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task GetTokenAsync_NoRefreshToken_ReturnsCachedEvenIfStale()
    {
        using var http = new HttpClient(new StubHandler((Func<HttpRequestMessage, HttpResponseMessage>)(_ =>
            throw new InvalidOperationException("Should not refresh without a refresh_token."))));
        var flow = BuildFlow(http);
        var clock = new FrozenClock(DateTimeOffset.UtcNow);
        var source = new AuthorizationCodeTokenSource(flow,
            new TokenResponse("atok", "Bearer", 60, null, null, null),
            clock.Now);
        clock.Advance(TimeSpan.FromSeconds(90));

        string token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("atok");
    }

    [Test]
    public async Task Invalidate_ForcesNextCallToRefresh()
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Ok("""{ "access_token": "fresh", "token_type": "Bearer", "expires_in": 3600 }""");
        }));
        var flow = BuildFlow(http);
        var clock = new FrozenClock(DateTimeOffset.UtcNow);
        var source = new AuthorizationCodeTokenSource(flow,
            new TokenResponse("atok", "Bearer", 3600, "rtok", null, null),
            clock.Now);

        await Assert.That(await source.GetTokenAsync(CancellationToken.None)).IsEqualTo("atok");
        source.Invalidate();
        string after = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(after).IsEqualTo("fresh");
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Constructor_RejectsEmptyAccessToken()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented)));
        var flow = BuildFlow(http);

        await Assert.That(() => new AuthorizationCodeTokenSource(flow,
                new TokenResponse(string.Empty, "Bearer", null, null, null, null)))
            .ThrowsExactly<ArgumentException>();
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
