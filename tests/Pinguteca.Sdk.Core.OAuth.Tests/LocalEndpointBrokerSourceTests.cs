using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class LocalEndpointBrokerSourceTests
{
    private static readonly Uri DefaultEndpoint = new("https://broker.example.com/issue");

    [Test]
    public async Task GetTokenAsync_PostsFormAndCachesAccessToken()
    {
        int calls = 0;
        string capturedFormBody = string.Empty;
        using var http = new HttpClient(new StubHandler(async req =>
        {
            Interlocked.Increment(ref calls);
            capturedFormBody = req.Content is null ? string.Empty : await req.Content.ReadAsStringAsync();
            return Ok("""{"access_token":"broker-issued","token_type":"Bearer","expires_in":3600}""");
        }));

        using var src = new LocalEndpointBrokerSource(new LocalEndpointBrokerConfig
        {
            HttpClient = http,
            Endpoint = DefaultEndpoint,
            Audience = "https://api.example.com",
            Scopes = new[] { "read" },
        });

        for (int i = 0; i < 3; i++)
        {
            string token = await src.GetTokenAsync(CancellationToken.None);
            await Assert.That(token).IsEqualTo("broker-issued");
        }
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(src.Origin).IsEqualTo("local-endpoint");
        await Assert.That(capturedFormBody).Contains("audience=https");
        await Assert.That(capturedFormBody).Contains("scope=read");
    }

    [Test]
    public async Task GetTokenAsync_SurfacesBrokerUnauthorised_OnHttp401()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("not authorised"),
        }));
        using var src = new LocalEndpointBrokerSource(new LocalEndpointBrokerConfig
        {
            HttpClient = http,
            Endpoint = DefaultEndpoint,
        });

        OAuthException? ex = null;
        try { _ = await src.GetTokenAsync(CancellationToken.None); }
        catch (OAuthException caught) { ex = caught; }

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.BrokerUnauthorised);
        await Assert.That(ex.HttpStatusCode).IsEqualTo(401);
    }

    [Test]
    public async Task GetTokenAsync_SurfacesBrokerUnavailable_OnHttp5xx()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom"),
        }));
        using var src = new LocalEndpointBrokerSource(new LocalEndpointBrokerConfig
        {
            HttpClient = http,
            Endpoint = DefaultEndpoint,
        });

        OAuthException? ex = null;
        try { _ = await src.GetTokenAsync(CancellationToken.None); }
        catch (OAuthException caught) { ex = caught; }

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.BrokerUnavailable);
    }

    [Test]
    public async Task Constructor_RejectsPlaintextNonLoopbackEndpoint()
    {
        using var http = new HttpClient();
        ArgumentException? ex = null;
        try
        {
            _ = new LocalEndpointBrokerSource(new LocalEndpointBrokerConfig
            {
                HttpClient = http,
                Endpoint = new Uri("http://broker.example.com/issue"),
            });
        }
        catch (ArgumentException caught) { ex = caught; }
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Constructor_AllowsPlaintextLoopback()
    {
        using var http = new HttpClient();
        using var src = new LocalEndpointBrokerSource(new LocalEndpointBrokerConfig
        {
            HttpClient = http,
            Endpoint = new Uri("http://127.0.0.1:8200/v1/auth/token"),
        });
        await Assert.That(src.Origin).IsEqualTo("local-endpoint");
    }

    [Test]
    public async Task Invalidate_ForcesFreshBrokerExchange()
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            int n = Interlocked.Increment(ref calls);
            string token = n == 1 ? "first" : "second";
            return Ok($$$"""{"access_token":"{{{token}}}","token_type":"Bearer","expires_in":3600}""");
        }));
        using var src = new LocalEndpointBrokerSource(new LocalEndpointBrokerConfig
        {
            HttpClient = http,
            Endpoint = DefaultEndpoint,
        });

        await Assert.That(await src.GetTokenAsync(CancellationToken.None)).IsEqualTo("first");
        src.Invalidate();
        await Assert.That(await src.GetTokenAsync(CancellationToken.None)).IsEqualTo("second");
        await Assert.That(calls).IsEqualTo(2);
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
