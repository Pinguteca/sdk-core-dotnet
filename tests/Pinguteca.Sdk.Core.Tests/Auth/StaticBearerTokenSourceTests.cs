using System;
using System.Threading;
using Pinguteca.Sdk.Core.Auth;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Auth;

public sealed class StaticBearerTokenSourceTests
{
    [Test]
    public async Task ReturnsConfiguredToken()
    {
        var source = new StaticBearerTokenSource("hunter2");

        var token = await source.GetTokenAsync(CancellationToken.None);

        await Assert.That(token).IsEqualTo("hunter2");
    }

    [Test]
    public async Task EmptyTokenThrows()
    {
        await Assert.That(() => new StaticBearerTokenSource(string.Empty))
            .ThrowsExactly<ArgumentException>();
    }
}
