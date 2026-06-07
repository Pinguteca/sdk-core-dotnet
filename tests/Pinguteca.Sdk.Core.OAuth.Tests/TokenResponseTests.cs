using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class TokenResponseTests
{
    [Test]
    public async Task ParseSuccess_FullPayload_PopulatesEveryField()
    {
        const string json = """
            {
              "access_token": "atok",
              "token_type": "Bearer",
              "expires_in": 3600,
              "refresh_token": "rtok",
              "scope": "read write",
              "id_token": "idtok"
            }
            """;

        var resp = TokenResponse.ParseSuccess(json);

        await Assert.That(resp.AccessToken).IsEqualTo("atok");
        await Assert.That(resp.TokenType).IsEqualTo("Bearer");
        await Assert.That(resp.ExpiresInSeconds).IsEqualTo(3600);
        await Assert.That(resp.RefreshToken).IsEqualTo("rtok");
        await Assert.That(resp.Scope).IsEqualTo("read write");
        await Assert.That(resp.IdToken).IsEqualTo("idtok");
    }

    [Test]
    public async Task ParseSuccess_MinimalPayload_OptionalFieldsAreNull()
    {
        const string json = """{ "access_token": "atok", "token_type": "Bearer" }""";

        var resp = TokenResponse.ParseSuccess(json);

        await Assert.That(resp.AccessToken).IsEqualTo("atok");
        await Assert.That(resp.TokenType).IsEqualTo("Bearer");
        await Assert.That(resp.ExpiresInSeconds).IsNull();
        await Assert.That(resp.RefreshToken).IsNull();
        await Assert.That(resp.Scope).IsNull();
        await Assert.That(resp.IdToken).IsNull();
    }

    [Test]
    public async Task ParseSuccess_MissingAccessToken_ThrowsInvalidResponse()
    {
        const string json = """{ "token_type": "Bearer" }""";

        var ex = await Assert.That(() => TokenResponse.ParseSuccess(json))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidResponse);
    }

    [Test]
    public async Task ParseSuccess_MissingTokenType_ThrowsInvalidResponse()
    {
        const string json = """{ "access_token": "atok" }""";

        var ex = await Assert.That(() => TokenResponse.ParseSuccess(json))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidResponse);
    }

    [Test]
    public async Task ParseSuccess_MalformedJson_ThrowsInvalidResponse()
    {
        const string json = "{not json";

        var ex = await Assert.That(() => TokenResponse.ParseSuccess(json))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidResponse);
    }

    [Test]
    public async Task ParseSuccess_JsonRootNotObject_ThrowsInvalidResponse()
    {
        const string json = "[]";

        var ex = await Assert.That(() => TokenResponse.ParseSuccess(json))
            .ThrowsExactly<OAuthException>();

        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidResponse);
    }
}
