using System.Diagnostics;
using Aspire.Hosting.Testing;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.IntegrationTests.Contract.V1;
using Pinguteca.Sdk.Core.Otel;

namespace Pinguteca.Sdk.Core.IntegrationTests;

/// <summary>
/// Integration coverage for <see cref="OtelInterceptor"/>. Subscribes
/// an in-process <see cref="ActivityListener"/> to the SDK's
/// activity source, makes a successful unary call through the
/// interceptor, and asserts the emitted activity carries the
/// OpenTelemetry RPC semantic-convention tags. Validates the
/// interceptor's wiring against a real gRPC stack rather than a
/// stubbed continuation.
/// </summary>
public sealed class OtelInterceptorTests
{
    [Test]
    public async Task EmitsClientActivityWithRpcAttributes()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Pinguteca.Sdk.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Pinguteca_Sdk_Core_IntegrationTests_AppHost>();
        await using var app = await builder.BuildAsync();
        await app.StartAsync();
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("fauxrpc")
            .WaitAsync(TimeSpan.FromSeconds(60));

        var endpoint = app.GetEndpoint("fauxrpc", "rpc");
        using var channel = GrpcChannel.ForAddress(endpoint);
        var client = new Harness.HarnessClient(channel.Intercept(new OtelInterceptor()));

        // Explicit `using` on the call so the wrapped AsyncUnaryCall
        // disposal fires the interceptor's activity.Dispose, which is
        // what triggers ActivityStopped on the listener.
        using (var call = client.EchoAsync(new EchoRequest { Message = "ping" }))
        {
            await call.ResponseAsync;
        }

        var activity = activities.SingleOrDefault();
        await Assert.That(activity).IsNotNull();
        // Grpc.Core's Method.FullName carries a leading slash by
        // convention, and the interceptor passes it through verbatim.
        await Assert.That(activity!.OperationName)
            .IsEqualTo("/pinguteca.sdk.core.integration.v1.Harness/Echo");
        await Assert.That(activity.Kind).IsEqualTo(ActivityKind.Client);
        await Assert.That(activity.GetTagItem("rpc.system")).IsEqualTo("grpc");
        await Assert.That(activity.GetTagItem("rpc.service"))
            .IsEqualTo("pinguteca.sdk.core.integration.v1.Harness");
        await Assert.That(activity.GetTagItem("rpc.method")).IsEqualTo("Echo");
        await Assert.That(activity.GetTagItem("rpc.grpc.status_code")).IsEqualTo(0);
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Ok);
    }
}
