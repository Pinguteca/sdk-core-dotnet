using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Errors;
using Pinguteca.Sdk.Core.Retry;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Retry;

public sealed class RetryInterceptorTests
{
    [Test]
    public async Task SucceedsOnFirstAttempt()
    {
        var harness = new RetryHarness();
        harness.Plan("ok");

        var result = await harness.Run();

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(harness.AttemptCount).IsEqualTo(1);
        await Assert.That(harness.Delays).IsEmpty();
    }

    [Test]
    public async Task RetriesOnUnavailableThenSucceeds()
    {
        var harness = new RetryHarness();
        harness.Plan(new RpcException(new Status(StatusCode.Unavailable, "transient")), "ok");

        var result = await harness.Run();

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(harness.AttemptCount).IsEqualTo(2);
        await Assert.That(harness.Delays.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GivesUpAfterMaxAttempts()
    {
        var options = new RetryOptions { MaxAttempts = 3, Random = new FixedRandom(0.5), Delay = NoDelay };
        var harness = new RetryHarness(options);
        harness.Plan(
            new RpcException(new Status(StatusCode.Unavailable, "x")),
            new RpcException(new Status(StatusCode.Unavailable, "y")),
            new RpcException(new Status(StatusCode.Unavailable, "z")));

        var error = await Assert.That(async () => await harness.Run()).ThrowsExactly<SdkError>();
        await Assert.That(error!.Code).IsEqualTo(SdkErrorCode.Unavailable);
        await Assert.That(harness.AttemptCount).IsEqualTo(3);
    }

    [Test]
    public async Task NonRetryableFailsImmediately()
    {
        var harness = new RetryHarness();
        harness.Plan(new RpcException(new Status(StatusCode.PermissionDenied, "no")));

        var error = await Assert.That(async () => await harness.Run()).ThrowsExactly<SdkError>();
        await Assert.That(error!.Code).IsEqualTo(SdkErrorCode.PermissionDenied);
        await Assert.That(harness.AttemptCount).IsEqualTo(1);
        await Assert.That(harness.Delays).IsEmpty();
    }

    [Test]
    public async Task HonorsRetryAfterFromTrailers()
    {
        var trailers = new Metadata { { "retry-after", "1.25" } };
        var harness = new RetryHarness();
        harness.Plan(new RpcException(new Status(StatusCode.Unavailable, "slow"), trailers), "ok");

        await harness.Run();

        await Assert.That(harness.Delays.Count).IsEqualTo(1);
        await Assert.That(harness.Delays[0]).IsEqualTo(TimeSpan.FromSeconds(1.25));
    }

    [Test]
    public async Task IgnoresRetryAfterWhenDisabled()
    {
        var options = new RetryOptions
        {
            HonorRetryAfter = false,
            BaseDelay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromMilliseconds(50),
            // Random=1.0 draws the ceiling; with full jitter and a flat
            // ceiling at BaseDelay the locally-computed delay is 50ms, not
            // the 10s the server asked for via retry-after.
            Random = new FixedRandom(1.0 - 1e-9),
            Delay = NoDelay,
        };
        var harness = new RetryHarness(options);
        var trailers = new Metadata { { "retry-after", "10" } };
        harness.Plan(new RpcException(new Status(StatusCode.Unavailable, "slow"), trailers), "ok");

        await harness.Run();

        await Assert.That(harness.Delays[0].TotalMilliseconds).IsLessThanOrEqualTo(50.0);
        await Assert.That(harness.Delays[0].TotalMilliseconds).IsGreaterThanOrEqualTo(49.0);
    }

    [Test]
    public async Task ConstructorRejectsZeroMaxAttempts()
    {
        await Assert.That(() => new RetryInterceptor(new RetryOptions { MaxAttempts = 0 }))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ConstructorRejectsSubUnitMultiplier()
    {
        await Assert.That(() => new RetryInterceptor(new RetryOptions { Multiplier = 0.5 }))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task DecorrelatedStrategy_FlowsThroughInterceptor()
    {
        var options = new RetryOptions
        {
            Strategy = RetryStrategy.Decorrelated,
            BaseDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(30),
            DecorrelationFactor = 3.0,
            Random = new FixedRandom(0.0),
            Delay = NoDelay,
        };
        var harness = new RetryHarness(options);
        harness.Plan(new RpcException(new Status(StatusCode.Unavailable, "x")), "ok");

        await harness.Run();

        // With random=0.0 the decorrelated draw collapses to BaseDelay.
        await Assert.That(harness.Delays[0]).IsEqualTo(TimeSpan.FromMilliseconds(100));
    }

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;
}

internal sealed class RetryHarness
{
    private readonly RetryInterceptor _interceptor;
    private readonly Queue<object> _plan = new();
    public List<TimeSpan> Delays { get; } = [];
    public int AttemptCount { get; private set; }

    public RetryHarness(RetryOptions? options = null)
    {
        var source = options ?? new RetryOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10),
            Random = new FixedRandom(0.0),
        };
        var inner = source.Delay;
        var opts = new RetryOptions
        {
            MaxAttempts = source.MaxAttempts,
            BaseDelay = source.BaseDelay,
            MaxDelay = source.MaxDelay,
            MinDelay = source.MinDelay,
            Multiplier = source.Multiplier,
            DecorrelationFactor = source.DecorrelationFactor,
            Strategy = source.Strategy,
            HonorRetryAfter = source.HonorRetryAfter,
            IsRetryable = source.IsRetryable,
            Random = source.Random,
            Delay = (delay, ct) =>
            {
                Delays.Add(delay);
                return inner is null ? Task.CompletedTask : inner(delay, ct);
            },
        };
        _interceptor = new RetryInterceptor(opts);
    }

    public void Plan(params object[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            _plan.Enqueue(outcome);
        }
    }

    public Task<string> Run()
    {
        var request = "req";
        var method = new Method<string, string>(
            MethodType.Unary,
            "svc",
            "m",
            Marshallers.StringMarshaller,
            Marshallers.StringMarshaller);
        var context = new ClientInterceptorContext<string, string>(method, host: null, new CallOptions());

        var call = _interceptor.AsyncUnaryCall(request, context, ContinueOnce);
        return call.ResponseAsync;
    }

    private AsyncUnaryCall<string> ContinueOnce(string request, ClientInterceptorContext<string, string> context)
    {
        AttemptCount++;
        if (_plan.Count == 0)
        {
            throw new InvalidOperationException("RetryHarness ran out of planned outcomes.");
        }
        var outcome = _plan.Dequeue();
        var response = outcome switch
        {
            string ok => Task.FromResult(ok),
            RpcException ex => Task.FromException<string>(ex),
            _ => throw new InvalidOperationException("Unsupported outcome type."),
        };
        return new AsyncUnaryCall<string>(
            response,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
    }
}
