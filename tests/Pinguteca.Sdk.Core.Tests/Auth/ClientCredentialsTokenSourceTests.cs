// Tests exercise Pinguteca.Sdk.Core.Auth.ClientCredentialsTokenSource,
// which is marked [Obsolete] now that the canonical home is the
// Pinguteca.Sdk.Core.OAuth package. The overlap window keeps this
// surface alive for one minor so consumers can migrate; the tests
// keep validating the legacy shape until it is deleted.
#pragma warning disable CS0618 // Type or member is obsolete

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinguteca.Sdk.Core.Auth;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Auth;

public sealed class ClientCredentialsTokenSourceTests
{
    private static readonly Uri _tokenUrl = new("https://idp.example/token");

    [Test]
    public async Task FetchesAndReturnsToken()
    {
        var handler = new StubHandler(_ => Json("{\"access_token\":\"t1\",\"expires_in\":3600,\"token_type\":\"Bearer\"}"));
        using var http = new HttpClient(handler);
        using var source = new ClientCredentialsTokenSource(
            new ClientCredentialsOptions
            {
                TokenUrl = _tokenUrl,
                ClientId = "id",
                ClientSecret = "secret",
                HttpClient = http,
            },
            utcNow: () => new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));

        var token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("t1");
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task CachesTokenUntilSkewWindow()
    {
        var handler = new StubHandler(_ => Json("{\"access_token\":\"t1\",\"expires_in\":3600}"));
        using var http = new HttpClient(handler);
        var now = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        using var source = new ClientCredentialsTokenSource(
            new ClientCredentialsOptions
            {
                TokenUrl = _tokenUrl,
                ClientId = "id",
                ClientSecret = "secret",
                HttpClient = http,
                RefreshSkew = TimeSpan.FromSeconds(60),
            },
            utcNow: () => now);

        await source.GetTokenAsync(CancellationToken.None);
        await source.GetTokenAsync(CancellationToken.None);
        await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task RefreshesAfterSkewWindow()
    {
        var bodies = new Queue<string>(new[]
        {
            "{\"access_token\":\"first\",\"expires_in\":120}",
            "{\"access_token\":\"second\",\"expires_in\":120}",
        });
        var handler = new StubHandler(_ => Json(bodies.Dequeue()));
        using var http = new HttpClient(handler);

        var clock = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        using var source = new ClientCredentialsTokenSource(
            new ClientCredentialsOptions
            {
                TokenUrl = _tokenUrl,
                ClientId = "id",
                ClientSecret = "secret",
                HttpClient = http,
                RefreshSkew = TimeSpan.FromSeconds(60),
            },
            utcNow: () => clock);

        var first = await source.GetTokenAsync(CancellationToken.None);
        clock = clock.AddSeconds(70); // inside the 60s skew of 120s expiry.
        var second = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(first).IsEqualTo("first");
        await Assert.That(second).IsEqualTo("second");
        await Assert.That(handler.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task InvalidateForcesRefreshOnNextCall()
    {
        var bodies = new Queue<string>(new[]
        {
            "{\"access_token\":\"first\",\"expires_in\":3600}",
            "{\"access_token\":\"second\",\"expires_in\":3600}",
        });
        var handler = new StubHandler(_ => Json(bodies.Dequeue()));
        using var http = new HttpClient(handler);
        using var source = new ClientCredentialsTokenSource(
            new ClientCredentialsOptions
            {
                TokenUrl = _tokenUrl,
                ClientId = "id",
                ClientSecret = "secret",
                HttpClient = http,
            },
            utcNow: () => new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));

        await source.GetTokenAsync(CancellationToken.None);
        source.Invalidate();
        var afterRotate = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(afterRotate).IsEqualTo("second");
        await Assert.That(handler.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task SendsBasicAuthByDefault()
    {
        var handler = new StubHandler(req =>
        {
            var auth = req.Headers.Authorization;
            if (auth?.Scheme != "Basic" || string.IsNullOrEmpty(auth.Parameter))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter));
            if (raw != "id:secret")
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            return Json("{\"access_token\":\"t1\",\"expires_in\":3600}");
        });
        using var http = new HttpClient(handler);
        using var source = new ClientCredentialsTokenSource(
            new ClientCredentialsOptions
            {
                TokenUrl = _tokenUrl,
                ClientId = "id",
                ClientSecret = "secret",
                HttpClient = http,
            });

        var token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("t1");
    }

    [Test]
    public async Task InBodyStyleSendsCredentialsInForm()
    {
        var handler = new StubHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            if (!body.Contains("client_id=id") || !body.Contains("client_secret=secret"))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            if (req.Headers.Authorization is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }
            return Json("{\"access_token\":\"t1\",\"expires_in\":3600}");
        });
        using var http = new HttpClient(handler);
        using var source = new ClientCredentialsTokenSource(
            new ClientCredentialsOptions
            {
                TokenUrl = _tokenUrl,
                ClientId = "id",
                ClientSecret = "secret",
                HttpClient = http,
                AuthStyle = ClientAuthStyle.InBody,
            });

        var token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("t1");
    }

    [Test]
    public async Task NonSuccessStatusRaisesTokenEndpointException()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid_client\"}"),
        });
        using var http = new HttpClient(handler);
        using var source = new ClientCredentialsTokenSource(
            new ClientCredentialsOptions
            {
                TokenUrl = _tokenUrl,
                ClientId = "id",
                ClientSecret = "wrong",
                HttpClient = http,
            });

        await Assert.That(async () => await source.GetTokenAsync(CancellationToken.None))
            .ThrowsExactly<TokenEndpointException>();
    }

    [Test]
    public async Task IncludesScopesAndAdditionalParameters()
    {
        var handler = new StubHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            var hasScope = body.Contains("scope=api%3Aread+api%3Awrite");
            var hasAudience = body.Contains("audience=https%3A%2F%2Fapi.example");
            if (!hasScope || !hasAudience)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }
            return Json("{\"access_token\":\"t1\",\"expires_in\":3600}");
        });
        using var http = new HttpClient(handler);
        using var source = new ClientCredentialsTokenSource(
            new ClientCredentialsOptions
            {
                TokenUrl = _tokenUrl,
                ClientId = "id",
                ClientSecret = "secret",
                HttpClient = http,
                Scopes = new[] { "api:read", "api:write" },
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["audience"] = "https://api.example",
                },
            });

        var token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("t1");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;
    private int _calls;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this(req => Task.FromResult(respond(req)))
    {
    }

    public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public int CallCount => _calls;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return await _respond(request).ConfigureAwait(false);
    }
}
