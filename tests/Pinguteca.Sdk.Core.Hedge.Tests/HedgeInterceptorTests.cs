using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Hedge;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Hedge.Tests;

public sealed class HedgeInterceptorTests
{
    private static readonly Method<string, string> _method = new(
        MethodType.Unary,
        "svc.v1.Svc",
        "Read",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    private static readonly Func<TimeSpan, CancellationToken, Task> _instantDelay =
        static (_, _) => Task.CompletedTask;

    [Test]
    public async Task Constructor_NullOptions_Throws()
    {
        await Assert.That(() => new HedgeInterceptor(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_ZeroMaxAttempts_Throws()
    {
        await Assert.That(() => new HedgeInterceptor(new HedgeOptions { MaxAttempts = 0 }))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Constructor_NegativeDelay_Throws()
    {
        await Assert.That(() => new HedgeInterceptor(new HedgeOptions
        {
            Delay = TimeSpan.FromMilliseconds(-1),
        })).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task NullPredicate_PassesThrough()
    {
        var interceptor = new HedgeInterceptor(new HedgeOptions
        {
            DelayFunc = _instantDelay,
        });
        var fake = new FakeContinuation();
        fake.Enqueue(_ => Task.FromResult("ok"));

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(fake.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task Unknown_PassesThrough()
    {
        var interceptor = new HedgeInterceptor(new HedgeOptions
        {
            IsHedgeEligible = _ => HedgeEligibility.Unknown,
            DelayFunc = _instantDelay,
        });
        var fake = new FakeContinuation();
        fake.Enqueue(_ => Task.FromResult("ok"));

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(fake.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task NoSideEffects_FastPrimary_OnlyOneCall()
    {
        var interceptor = new HedgeInterceptor(new HedgeOptions
        {
            IsHedgeEligible = _ => HedgeEligibility.NoSideEffects,
            DelayFunc = (_, _) => Task.Delay(TimeSpan.FromSeconds(5)),
        });
        var fake = new FakeContinuation();
        fake.Enqueue(_ => Task.FromResult("first"));

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("first");
        await Assert.That(fake.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task NoSideEffects_StaggerFiresAllAttempts()
    {
        var primary = new TaskCompletionSource<string>();
        var second = new TaskCompletionSource<string>();
        var third = new TaskCompletionSource<string>();

        var interceptor = new HedgeInterceptor(new HedgeOptions
        {
            IsHedgeEligible = _ => HedgeEligibility.NoSideEffects,
            DelayFunc = _instantDelay,
            MaxAttempts = 3,
        });
        var fake = new FakeContinuation();
        fake.Enqueue(_ => primary.Task);
        fake.Enqueue(_ => second.Task);
        fake.Enqueue(_ => third.Task);

        var pending = Invoke(interceptor, fake);

        // Let the staggered launches happen.
        await Task.Delay(50);
        await Assert.That(fake.Calls).IsEqualTo(3);

        third.SetResult("third wins");
        var result = await pending;
        await Assert.That(result).IsEqualTo("third wins");
    }

    [Test]
    public async Task Idempotent_OptOut_PassesThrough()
    {
        var interceptor = new HedgeInterceptor(new HedgeOptions
        {
            IsHedgeEligible = _ => HedgeEligibility.Idempotent,
            HedgeIdempotent = false,
            DelayFunc = _instantDelay,
        });
        var fake = new FakeContinuation();
        fake.Enqueue(_ => Task.FromResult("ok"));

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(fake.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task Idempotent_OptIn_Hedges()
    {
        var primary = new TaskCompletionSource<string>();
        var second = new TaskCompletionSource<string>();

        var interceptor = new HedgeInterceptor(new HedgeOptions
        {
            IsHedgeEligible = _ => HedgeEligibility.Idempotent,
            HedgeIdempotent = true,
            DelayFunc = _instantDelay,
            MaxAttempts = 2,
        });
        var fake = new FakeContinuation();
        fake.Enqueue(_ => primary.Task);
        fake.Enqueue(_ => second.Task);

        var pending = Invoke(interceptor, fake);
        await Task.Delay(50);
        second.SetResult("hedge wins");
        var result = await pending;

        await Assert.That(result).IsEqualTo("hedge wins");
        await Assert.That(fake.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task MaxAttemptsOne_PassesThrough()
    {
        var interceptor = new HedgeInterceptor(new HedgeOptions
        {
            IsHedgeEligible = _ => HedgeEligibility.NoSideEffects,
            MaxAttempts = 1,
            DelayFunc = _instantDelay,
        });
        var fake = new FakeContinuation();
        fake.Enqueue(_ => Task.FromResult("ok"));

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(fake.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task AllAttemptsFail_ReturnsLastObservedError()
    {
        var primary = new TaskCompletionSource<string>();
        var second = new TaskCompletionSource<string>();
        var third = new TaskCompletionSource<string>();

        var interceptor = new HedgeInterceptor(new HedgeOptions
        {
            IsHedgeEligible = _ => HedgeEligibility.NoSideEffects,
            DelayFunc = _instantDelay,
            MaxAttempts = 3,
        });
        var fake = new FakeContinuation();
        fake.Enqueue(_ => primary.Task);
        fake.Enqueue(_ => second.Task);
        fake.Enqueue(_ => third.Task);

        var pending = Invoke(interceptor, fake);
        await Task.Delay(50);

        // Complete in a controlled order; the LAST one to finish is the
        // error that should be surfaced per RFC 0013.
        primary.SetException(new RpcException(new Status(StatusCode.Unavailable, "first")));
        await Task.Delay(20);
        second.SetException(new RpcException(new Status(StatusCode.Unavailable, "second")));
        await Task.Delay(20);
        third.SetException(new RpcException(new Status(StatusCode.Unavailable, "last")));

        RpcException? thrown = null;
        try
        {
            await pending;
        }
        catch (RpcException ex)
        {
            thrown = ex;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Status.Detail).IsEqualTo("last");
        await Assert.That(fake.Calls).IsEqualTo(3);
    }

    // ---------- helpers ----------

    private static async Task<string> Invoke(HedgeInterceptor interceptor, FakeContinuation fake)
    {
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall("req", context, fake.Continuation);
        try
        {
            return await call.ResponseAsync.ConfigureAwait(false);
        }
        finally
        {
            call.Dispose();
        }
    }
}

internal sealed class FakeContinuation
{
    private readonly Queue<Func<ClientInterceptorContext<string, string>, Task<string>>> _scripted = new();
    private int _calls;

    public int Calls => _calls;

    public void Enqueue(Func<ClientInterceptorContext<string, string>, Task<string>> producer)
    {
        _scripted.Enqueue(producer);
    }

    public AsyncUnaryCall<string> Continuation(string request, ClientInterceptorContext<string, string> context)
    {
        Interlocked.Increment(ref _calls);
        var producer = _scripted.Count > 0
            ? _scripted.Dequeue()
            : (_ => Task.FromException<string>(new InvalidOperationException("FakeContinuation: no more scripted calls")));
        var task = producer(context);
        return new AsyncUnaryCall<string>(
            task,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
    }
}
