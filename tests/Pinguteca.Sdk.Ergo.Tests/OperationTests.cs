using System;
using System.Threading;
using System.Threading.Tasks;
using Pinguteca.Sdk.Ergo;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Ergo.Tests;

public sealed class OperationTests
{
    private static Task InstantSleep(TimeSpan _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    [Test]
    public async Task WaitAsync_ReturnsResultOnImmediateDone()
    {
        var calls = 0;
        var op = new Operation<string>
        {
            Poll = _ =>
            {
                calls++;
                return Task.FromResult(new OperationStatus<string>(Done: true, Result: "ok"));
            },
            InitialDelay = TimeSpan.FromMilliseconds(1),
            SleepAsync = InstantSleep,
        };
        var result = await op.WaitAsync(CancellationToken.None);
        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task WaitAsync_PollsUntilDone()
    {
        var calls = 0;
        var op = new Operation<int>
        {
            Poll = _ =>
            {
                calls++;
                if (calls < 3)
                {
                    return Task.FromResult(new OperationStatus<int>(Done: false));
                }
                return Task.FromResult(new OperationStatus<int>(Done: true, Result: calls));
            },
            InitialDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10),
            SleepAsync = InstantSleep,
        };
        var result = await op.WaitAsync(CancellationToken.None);
        await Assert.That(result).IsEqualTo(3);
        await Assert.That(calls).IsEqualTo(3);
    }

    [Test]
    public async Task WaitAsync_RespectsCancellation()
    {
        var op = new Operation<string>
        {
            Poll = _ => Task.FromResult(new OperationStatus<string>(Done: false)),
            InitialDelay = TimeSpan.FromMilliseconds(1),
            SleepAsync = (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromCanceled(ct);
            },
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.That(async () => await op.WaitAsync(cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task WaitAsync_PropagatesPollError()
    {
        var sentinel = new InvalidOperationException("server boom");
        var op = new Operation<string>
        {
            Poll = _ => Task.FromException<OperationStatus<string>>(sentinel),
            SleepAsync = InstantSleep,
        };
        var ex = await Assert.That(async () => await op.WaitAsync(CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();
        await Assert.That(ex!.Message).IsEqualTo("server boom");
    }

    [Test]
    public async Task WaitAsync_PropagatesTerminalError()
    {
        var terminal = new InvalidOperationException("operation failed");
        var op = new Operation<string>
        {
            Poll = _ => Task.FromResult(new OperationStatus<string>(
                Done: true,
                Result: "partial",
                Error: terminal)),
            SleepAsync = InstantSleep,
        };
        await Assert.That(async () => await op.WaitAsync(CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task WaitAsync_HonoursRetryAfterHint()
    {
        var observed = new List<TimeSpan>();
        var calls = 0;
        var op = new Operation<string>
        {
            Poll = _ =>
            {
                calls++;
                if (calls == 1)
                {
                    return Task.FromResult(new OperationStatus<string>(
                        Done: false,
                        RetryAfter: TimeSpan.FromMilliseconds(250)));
                }
                return Task.FromResult(new OperationStatus<string>(Done: true, Result: "ok"));
            },
            InitialDelay = TimeSpan.FromHours(1),
            SleepAsync = (delay, _) =>
            {
                observed.Add(delay);
                return Task.CompletedTask;
            },
        };
        await op.WaitAsync(CancellationToken.None);
        await Assert.That(observed.Count).IsEqualTo(1);
        await Assert.That(observed[0]).IsEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public async Task WaitAsync_NoPollThrows()
    {
        var op = new Operation<string>();
        await Assert.That(async () => await op.WaitAsync(CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();
    }
}
