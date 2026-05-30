using System.Threading.Tasks;
using Pinguteca.Sdk.Ergo;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Ergo.Tests;

public sealed class IdGeneratorTests
{
    [Test]
    public async Task NewId_ReturnsHexOfExpectedLength()
    {
        var id = IdGenerator.NewId();
        await Assert.That(id.Length).IsEqualTo(IdGenerator.IdByteLength * 2);
    }

    [Test]
    public async Task NewId_ProducesDistinctValues()
    {
        var a = IdGenerator.NewId();
        var b = IdGenerator.NewId();
        await Assert.That(a).IsNotEqualTo(b);
    }
}
