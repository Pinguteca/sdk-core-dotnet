using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class HeaderPassthroughSourceTests
{
    [Test]
    public async Task GetTokenAsync_ReturnsSeededToken()
    {
        var src = new HeaderPassthroughSource("seeded");
        string tok = await src.GetTokenAsync(CancellationToken.None);
        await Assert.That(tok).IsEqualTo("seeded");
        await Assert.That(src.Origin).IsEqualTo("header-passthrough");
    }

    [Test]
    public async Task SetToken_ReplacesHeldValue()
    {
        var src = new HeaderPassthroughSource("first");
        src.SetToken("second");
        string tok = await src.GetTokenAsync(CancellationToken.None);
        await Assert.That(tok).IsEqualTo("second");
    }

    [Test]
    public async Task GetTokenAsync_UnboundSurfacesBrokerUnauthorised()
    {
        var src = new HeaderPassthroughSource();
        OAuthException? ex = null;
        try { _ = await src.GetTokenAsync(CancellationToken.None); }
        catch (OAuthException caught) { ex = caught; }
        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.BrokerUnauthorised);
    }

    [Test]
    public async Task Invalidate_ClearsHeldToken()
    {
        var src = new HeaderPassthroughSource("seeded");
        src.Invalidate();
        OAuthException? ex = null;
        try { _ = await src.GetTokenAsync(CancellationToken.None); }
        catch (OAuthException caught) { ex = caught; }
        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.ErrorCode).IsEqualTo(OAuthErrorCodes.BrokerUnauthorised);
    }

    [Test]
    public async Task SetToken_RejectsEmpty()
    {
        var src = new HeaderPassthroughSource();
        ArgumentException? ex = null;
        try { src.SetToken(string.Empty); }
        catch (ArgumentException caught) { ex = caught; }
        await Assert.That(ex).IsNotNull();
    }
}
