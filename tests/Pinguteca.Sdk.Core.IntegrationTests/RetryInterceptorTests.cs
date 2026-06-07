using System.Diagnostics;
using Aspire.Hosting.Testing;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.Errors;
using Pinguteca.Sdk.Core.IntegrationTests.Contract.V1;
using Pinguteca.Sdk.Core.IntegrationTests.Fixtures;
using Pinguteca.Sdk.Core.Retry;

namespace Pinguteca.Sdk.Core.IntegrationTests;

/// <summary>
/// Integration coverage for <see cref="RetryInterceptor"/>. Configures
/// the FauxRPC harness so the unary Echo method always returns
/// <c>Unavailable</c>, then asserts that the SDK retries up to the
/// configured cap and surfaces an <see cref="SdkError"/> carrying the
/// canonical <see cref="SdkErrorCode.Unavailable"/> code.
/// </summary>
public sealed class RetryInterceptorTests
{
    private const string EchoTarget = "pinguteca.sdk.core.integration.v1.Harness/Echo";

    [Test]
    public async Task RetriesOnUnavailable_SurfacesSdkErrorAfterExhaustion()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Pinguteca_Sdk_Core_IntegrationTests_AppHost>();
        await using var app = await builder.BuildAsync();
        await app.StartAsync();
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("fauxrpc")
            .WaitAsync(TimeSpan.FromSeconds(60));

        var endpoint = app.GetEndpoint("fauxrpc", "rpc");
        await FauxRpcStubs.AddErrorStubAsync(
            endpoint,
            EchoTarget,
            errorCode: "ERROR_CODE_UNAVAILABLE",
            message: "test injected");

        using var channel = GrpcChannel.ForAddress(endpoint);
        var retry = new RetryInterceptor(new RetryOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromMilliseconds(200),
            MinDelay = TimeSpan.FromMilliseconds(30),
            HonorRetryAfter = false,
        });
        var client = new Harness.HarnessClient(channel.Intercept(retry));

        var stopwatch = Stopwatch.StartNew();
        var error = await Assert.That(async () =>
                await client.EchoAsync(new EchoRequest { Message = "ping" }))
            .ThrowsExactly<SdkError>();
        stopwatch.Stop();

        await Assert.That(error!.Code).IsEqualTo(SdkErrorCode.Unavailable);
        // Two retries with MinDelay=30ms each implies the call could
        // not have surfaced in under ~50ms. A faster return would
        // mean the retry interceptor short-circuited.
        await Assert.That(stopwatch.Elapsed).IsGreaterThan(TimeSpan.FromMilliseconds(50));
    }
}
