using System.Threading.Tasks;
using Grpc.Core;
using Pinguteca.Sdk.Ergo;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Ergo.Tests;

public sealed class ComposedOpTests
{
    [Test]
    public async Task New_GeneratesDistinctIdsAndCorrelations()
    {
        var a = ComposedOp.New();
        var b = ComposedOp.New();
        await Assert.That(a.Id).IsNotEqualTo(b.Id);
        await Assert.That(a.Correlation).IsNotEqualTo(b.Correlation);
    }

    [Test]
    public async Task Continue_InheritsSuppliedCorrelation()
    {
        var op = ComposedOp.Continue("trace-42");
        await Assert.That(op.Correlation).IsEqualTo("trace-42");
    }

    [Test]
    public async Task Continue_GeneratesCorrelationWhenNullOrEmpty()
    {
        var a = ComposedOp.Continue(null);
        var b = ComposedOp.Continue(string.Empty);
        await Assert.That(a.Correlation).IsNotEqualTo(string.Empty);
        await Assert.That(b.Correlation).IsNotEqualTo(string.Empty);
        await Assert.That(a.Correlation).IsNotEqualTo(b.Correlation);
    }

    [Test]
    public async Task RunAsync_DerivesPerLegKeys()
    {
        var op = ComposedOp.New();
        var seen = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            await op.RunAsync(new CallOptions(), opts =>
            {
                var key = opts.Headers!.GetValue(ComposedOp.IdempotencyKeyHeader);
                seen.Add(key!);
                return Task.FromResult(0);
            });
        }
        await Assert.That(seen.Count).IsEqualTo(3);
        await Assert.That(seen[0]).IsEqualTo($"{op.Id}/0");
        await Assert.That(seen[1]).IsEqualTo($"{op.Id}/1");
        await Assert.That(seen[2]).IsEqualTo($"{op.Id}/2");
    }

    [Test]
    public async Task RunAsync_PropagatesCorrelationHeader()
    {
        var op = ComposedOp.Continue("trace-7");
        string? observed = null;
        await op.RunAsync(new CallOptions(), opts =>
        {
            observed = opts.Headers!.GetValue(ComposedOp.CorrelationIdHeader);
            return Task.FromResult(0);
        });
        await Assert.That(observed).IsEqualTo("trace-7");
    }

    [Test]
    public async Task RunAsync_PreservesExistingHeaders()
    {
        var op = ComposedOp.New();
        var baseHeaders = new Metadata { { "x-tenant-id", "t-9" } };
        var baseOptions = new CallOptions(headers: baseHeaders);
        string? observed = null;
        await op.RunAsync(baseOptions, opts =>
        {
            observed = opts.Headers!.GetValue("x-tenant-id");
            return Task.FromResult(0);
        });
        await Assert.That(observed).IsEqualTo("t-9");
    }

    [Test]
    public async Task NextLegOptions_ReturnsScopedHeaders()
    {
        var op = ComposedOp.New();
        var legA = op.NextLegOptions(new CallOptions());
        var legB = op.NextLegOptions(new CallOptions());

        var keyA = legA.Headers!.GetValue(ComposedOp.IdempotencyKeyHeader);
        var keyB = legB.Headers!.GetValue(ComposedOp.IdempotencyKeyHeader);
        await Assert.That(keyA).IsEqualTo($"{op.Id}/0");
        await Assert.That(keyB).IsEqualTo($"{op.Id}/1");
    }
}
