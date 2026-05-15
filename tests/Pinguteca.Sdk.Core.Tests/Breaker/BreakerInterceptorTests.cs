using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Breaker;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Breaker;

public sealed class BreakerInterceptorTests
{
    private static readonly Method<string, string> _method = new(
        MethodType.Unary,
        "svc",
        "m",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    [Test]
    public async Task ClosedBreakerAllowsCalls()
    {
        var clock = new FakeClock();
        var interceptor = new BreakerInterceptor(
            new BreakerOptions { UtcNow = clock.Now });

        var call = interceptor.AsyncUnaryCall("req", NewContext(), (_, _) => SuccessCall("ok"));
        var response = await call.ResponseAsync;

        await Assert.That(response).IsEqualTo("ok");
    }

    [Test]
    public async Task OpensAfterThresholdFailures()
    {
        var clock = new FakeClock();
        var interceptor = new BreakerInterceptor(new BreakerOptions
        {
            FailureRateThreshold = 0.5,
            MinSamples = 4,
            OpenDuration = TimeSpan.FromSeconds(5),
            UtcNow = clock.Now,
        });

        for (var i = 0; i < 4; i++)
        {
            var failingCall = interceptor.AsyncUnaryCall("req", NewContext(),
                (_, _) => FailingCall(StatusCode.Unavailable));
            await Assert.ThrowsAsync<RpcException>(async () => await failingCall.ResponseAsync);
        }

        var blocked = interceptor.AsyncUnaryCall("req", NewContext(), (_, _) => SuccessCall("ok"));
        var error = await Assert.ThrowsAsync<RpcException>(async () => await blocked.ResponseAsync);
        await Assert.That(error.StatusCode).IsEqualTo(StatusCode.Unavailable);
        await Assert.That(error.Status.Detail).IsEqualTo("circuit breaker open");
        await Assert.That(error.Trailers.GetValue("retry-after")).IsNotNull();
    }

    [Test]
    public async Task HalfOpenProbeClosesOnSuccess()
    {
        var clock = new FakeClock();
        var interceptor = new BreakerInterceptor(new BreakerOptions
        {
            FailureRateThreshold = 0.5,
            MinSamples = 2,
            OpenDuration = TimeSpan.FromSeconds(5),
            UtcNow = clock.Now,
        });

        for (var i = 0; i < 2; i++)
        {
            var call = interceptor.AsyncUnaryCall("req", NewContext(),
                (_, _) => FailingCall(StatusCode.Unavailable));
            await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        }

        clock.Advance(TimeSpan.FromSeconds(6));

        var probe = interceptor.AsyncUnaryCall("req", NewContext(), (_, _) => SuccessCall("probe"));
        await Assert.That(await probe.ResponseAsync).IsEqualTo("probe");

        var follow = interceptor.AsyncUnaryCall("req", NewContext(), (_, _) => SuccessCall("post"));
        await Assert.That(await follow.ResponseAsync).IsEqualTo("post");
    }

    [Test]
    public async Task HalfOpenProbeReopensOnFailure()
    {
        var clock = new FakeClock();
        var interceptor = new BreakerInterceptor(new BreakerOptions
        {
            FailureRateThreshold = 0.5,
            MinSamples = 2,
            OpenDuration = TimeSpan.FromSeconds(5),
            UtcNow = clock.Now,
        });

        for (var i = 0; i < 2; i++)
        {
            var call = interceptor.AsyncUnaryCall("req", NewContext(),
                (_, _) => FailingCall(StatusCode.Unavailable));
            await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        }

        clock.Advance(TimeSpan.FromSeconds(6));

        var probe = interceptor.AsyncUnaryCall("req", NewContext(),
            (_, _) => FailingCall(StatusCode.Unavailable));
        await Assert.ThrowsAsync<RpcException>(async () => await probe.ResponseAsync);

        var blocked = interceptor.AsyncUnaryCall("req", NewContext(), (_, _) => SuccessCall("ok"));
        var error = await Assert.ThrowsAsync<RpcException>(async () => await blocked.ResponseAsync);
        await Assert.That(error.Status.Detail).IsEqualTo("circuit breaker open");
    }

    private static ClientInterceptorContext<string, string> NewContext() =>
        new(_method, host: null, new CallOptions());

    private static AsyncUnaryCall<string> SuccessCall(string body) =>
        new(
            Task.FromResult(body),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });

    private static AsyncUnaryCall<string> FailingCall(StatusCode code) =>
        new(
            Task.FromException<string>(new RpcException(new Status(code, "boom"))),
            Task.FromResult(new Metadata()),
            () => new Status(code, "boom"),
            () => [],
            () => { });

    private sealed class FakeClock
    {
        private DateTimeOffset _now = new(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Current() => _now;
        public Func<DateTimeOffset> Now => Current;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
