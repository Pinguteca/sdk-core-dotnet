using System;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Assertions.Extensions.Throws;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class OAuthExceptionTests
{
    [Test]
    public async Task FromTokenEndpointError_FullBody_MapsEveryField()
    {
        const string body = """
            {
              "error": "invalid_grant",
              "error_description": "Refresh token expired.",
              "error_uri": "https://idp.example.com/docs/errors/invalid_grant"
            }
            """;

        var ex = OAuthException.FromTokenEndpointError(400, body);

        await Assert.That(ex.ErrorCode).IsEqualTo(OAuthErrorCodes.InvalidGrant);
        await Assert.That(ex.ErrorDescription).IsEqualTo("Refresh token expired.");
        await Assert.That(ex.ErrorUri!.ToString()).IsEqualTo("https://idp.example.com/docs/errors/invalid_grant");
        await Assert.That(ex.HttpStatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task FromTokenEndpointError_EmptyBody_FallsBackToServerError()
    {
        var ex = OAuthException.FromTokenEndpointError(503, string.Empty);

        await Assert.That(ex.ErrorCode).IsEqualTo(OAuthErrorCodes.ServerError);
        await Assert.That(ex.HttpStatusCode).IsEqualTo(503);
    }

    [Test]
    public async Task FromTokenEndpointError_NonJsonBody_FallsBackToServerError()
    {
        var ex = OAuthException.FromTokenEndpointError(502, "Bad Gateway");

        await Assert.That(ex.ErrorCode).IsEqualTo(OAuthErrorCodes.ServerError);
        await Assert.That(ex.ErrorDescription).IsEqualTo("Bad Gateway");
        await Assert.That(ex.HttpStatusCode).IsEqualTo(502);
    }

    [Test]
    public async Task FromTokenEndpointError_BodyWithoutErrorField_FallsBackToServerError()
    {
        const string body = """{ "unrelated": "field" }""";

        var ex = OAuthException.FromTokenEndpointError(400, body);

        await Assert.That(ex.ErrorCode).IsEqualTo(OAuthErrorCodes.ServerError);
        await Assert.That(ex.HttpStatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Constructor_RejectsEmptyErrorCode()
    {
        await Assert.That(() => new OAuthException(string.Empty))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Message_IncludesCodeAndDescription()
    {
        var ex = new OAuthException(OAuthErrorCodes.InvalidClient, "Missing secret.");

        await Assert.That(ex.Message).Contains(OAuthErrorCodes.InvalidClient);
        await Assert.That(ex.Message).Contains("Missing secret.");
    }
}
