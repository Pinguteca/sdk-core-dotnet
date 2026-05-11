using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Timeouts;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Timeouts;

public sealed class TimeoutInterceptorTests
{
    private static readonly Method<string, string> _method = new(
        MethodType.Unary,
        "svc",
        "m",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    [Test]
    public async Task StampsDeadlineWhenAbsent()
    {
        var fixedNow = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);
        var interceptor = new TimeoutInterceptor(
            new TimeoutOptions { Default = TimeSpan.FromSeconds(5) },
            () => fixedNow);

        DateTime? observed = null;
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (req, ctx) =>
            {
                observed = ctx.Options.Deadline;
                return EmptyCall();
            });
        await call.ResponseAsync;

        await Assert.That(observed).IsEqualTo(fixedNow.AddSeconds(5));
    }

    [Test]
    public async Task PreservesCallerDeadline()
    {
        var fixedNow = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);
        var callerDeadline = fixedNow.AddSeconds(1);
        var interceptor = new TimeoutInterceptor(
            new TimeoutOptions { Default = TimeSpan.FromSeconds(60) },
            () => fixedNow);

        DateTime? observed = null;
        var context = new ClientInterceptorContext<string, string>(
            _method,
            host: null,
            new CallOptions().WithDeadline(callerDeadline));
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (req, ctx) =>
            {
                observed = ctx.Options.Deadline;
                return EmptyCall();
            });
        await call.ResponseAsync;

        await Assert.That(observed).IsEqualTo(callerDeadline);
    }

    [Test]
    public async Task PassesThroughWhenDefaultIsNull()
    {
        var interceptor = new TimeoutInterceptor(new TimeoutOptions { Default = null });

        DateTime? observed = null;
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (req, ctx) =>
            {
                observed = ctx.Options.Deadline;
                return EmptyCall();
            });
        await call.ResponseAsync;

        await Assert.That(observed).IsNull();
    }

    private static AsyncUnaryCall<string> EmptyCall() =>
        new(
            Task.FromResult("ok"),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
}
