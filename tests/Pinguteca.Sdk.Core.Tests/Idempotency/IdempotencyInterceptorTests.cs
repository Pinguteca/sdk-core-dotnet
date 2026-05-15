using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Idempotency;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Idempotency;

public sealed class IdempotencyInterceptorTests
{
    private static readonly Method<string, string> _method = new(
        MethodType.Unary,
        "svc",
        "m",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    [Test]
    public async Task StampsKeyHeaderWhenAbsent()
    {
        var interceptor = new IdempotencyInterceptor(
            new IdempotencyOptions { KeyFactory = () => "fixed-key" });

        Metadata? observed = null;
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (_, ctx) =>
            {
                observed = ctx.Options.Headers;
                return SuccessCall();
            });
        await call.ResponseAsync;

        await Assert.That(observed!.GetValue("Idempotency-Key")).IsEqualTo("fixed-key");
    }

    [Test]
    public async Task PreservesCallerProvidedKey()
    {
        var interceptor = new IdempotencyInterceptor(
            new IdempotencyOptions { KeyFactory = () => "generated" });

        var callerHeaders = new Metadata { { "Idempotency-Key", "caller-key" } };
        Metadata? observed = null;
        var context = new ClientInterceptorContext<string, string>(
            _method,
            host: null,
            new CallOptions(headers: callerHeaders));
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (_, ctx) =>
            {
                observed = ctx.Options.Headers;
                return SuccessCall();
            });
        await call.ResponseAsync;

        await Assert.That(observed!.GetValue("Idempotency-Key")).IsEqualTo("caller-key");
    }

    [Test]
    public async Task DoesNotMutateCallerMetadata()
    {
        var interceptor = new IdempotencyInterceptor(
            new IdempotencyOptions { KeyFactory = () => "fixed-key" });

        var callerHeaders = new Metadata();
        var context = new ClientInterceptorContext<string, string>(
            _method,
            host: null,
            new CallOptions(headers: callerHeaders));
        var call = interceptor.AsyncUnaryCall("req", context, (_, _) => SuccessCall());
        await call.ResponseAsync;

        await Assert.That(callerHeaders.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DefaultKeyFactoryProducesUuid()
    {
        var interceptor = new IdempotencyInterceptor();

        Metadata? observed = null;
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (_, ctx) =>
            {
                observed = ctx.Options.Headers;
                return SuccessCall();
            });
        await call.ResponseAsync;

        var key = observed!.GetValue("Idempotency-Key");
        await Assert.That(key).IsNotNull();
        await Assert.That(System.Guid.TryParse(key, out _)).IsTrue();
    }

    private static AsyncUnaryCall<string> SuccessCall() =>
        new(
            Task.FromResult("ok"),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });
}
