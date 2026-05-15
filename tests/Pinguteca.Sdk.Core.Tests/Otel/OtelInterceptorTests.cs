using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Otel;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Otel;

public sealed class OtelInterceptorTests
{
    private static readonly Method<string, string> _method = new(
        MethodType.Unary,
        "svc",
        "m",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    [Test]
    public async Task EmitsClientSpanWithRpcSemconvOnSuccess()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var recorded = new List<Activity>();
        using var listener = StartListener(sourceName, recorded);

        var interceptor = new OtelInterceptor(new OtelOptions { ActivitySourceName = sourceName });
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());

        var call = interceptor.AsyncUnaryCall("req", context, (_, _) => SuccessCall());
        await call.ResponseAsync;
        call.Dispose();

        await Assert.That(recorded.Count).IsEqualTo(1);
        var activity = recorded[0];
        await Assert.That(activity.Kind).IsEqualTo(ActivityKind.Client);
        await Assert.That(activity.DisplayName).IsEqualTo("/svc/m");
        await Assert.That(activity.GetTagItem("rpc.system")).IsEqualTo("grpc");
        await Assert.That(activity.GetTagItem("rpc.service")).IsEqualTo("svc");
        await Assert.That(activity.GetTagItem("rpc.method")).IsEqualTo("m");
        await Assert.That(activity.GetTagItem("rpc.grpc.status_code")).IsEqualTo(0);
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Ok);
    }

    [Test]
    public async Task TagsErrorStatusOnRpcException()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var recorded = new List<Activity>();
        using var listener = StartListener(sourceName, recorded);

        var interceptor = new OtelInterceptor(new OtelOptions { ActivitySourceName = sourceName });
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());

        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (_, _) => FailingCall(new Status(StatusCode.Unavailable, "down")));

        await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        call.Dispose();

        await Assert.That(recorded.Count).IsEqualTo(1);
        var activity = recorded[0];
        await Assert.That(activity.GetTagItem("rpc.grpc.status_code"))
            .IsEqualTo((int)StatusCode.Unavailable);
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    private static ActivityListener StartListener(string sourceName, List<Activity> recorded)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = recorded.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static AsyncUnaryCall<string> SuccessCall() =>
        new(
            Task.FromResult("ok"),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });

    private static AsyncUnaryCall<string> FailingCall(Status status) =>
        new(
            Task.FromException<string>(new RpcException(status)),
            Task.FromResult(new Metadata()),
            () => status,
            () => [],
            () => { });
}
