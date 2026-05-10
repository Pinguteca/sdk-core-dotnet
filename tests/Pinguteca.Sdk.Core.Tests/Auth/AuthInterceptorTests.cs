using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Auth;
using Pinguteca.Sdk.Core.Errors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Auth;

public sealed class AuthInterceptorTests
{
    private static readonly Method<string, string> _method = new(
        MethodType.Unary,
        "Pinguteca.Demo",
        "Ping",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    [Test]
    public async Task AttachesBearerHeader()
    {
        var interceptor = new AuthInterceptor(new AuthOptions
        {
            Source = new StaticBearerTokenSource("t1"),
        });

        string? observed = null;
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (req, ctx) =>
            {
                observed = ctx.Options.Headers?.GetValue("authorization");
                return ResponseCall("ok");
            });

        await call.ResponseAsync;

        await Assert.That(observed).IsEqualTo("Bearer t1");
    }

    [Test]
    public async Task UsesCustomFormatterAndHeader()
    {
        var interceptor = new AuthInterceptor(new AuthOptions
        {
            Source = new StaticBearerTokenSource("key-xyz"),
            HeaderName = "X-Api-Key",
            FormatHeader = static t => t,
        });

        string? observed = null;
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (req, ctx) =>
            {
                observed = ctx.Options.Headers?.GetValue("x-api-key");
                return ResponseCall("ok");
            });

        await call.ResponseAsync;

        await Assert.That(observed).IsEqualTo("key-xyz");
    }

    [Test]
    public async Task SkipPredicateBypassesAttachment()
    {
        var interceptor = new AuthInterceptor(new AuthOptions
        {
            Source = new StaticBearerTokenSource("t1"),
            Skip = static method => method.EndsWith("/Ping", StringComparison.Ordinal),
        });

        Metadata? observed = null;
        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (req, ctx) =>
            {
                observed = ctx.Options.Headers;
                return ResponseCall("ok");
            });

        await call.ResponseAsync;

        await Assert.That(observed).IsNull();
    }

    [Test]
    public async Task TokenSourceFailureRaisesUnauthenticatedSdkError()
    {
        var interceptor = new AuthInterceptor(new AuthOptions
        {
            Source = new FailingTokenSource(),
        });

        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (req, ctx) => ResponseCall("ok"));

        var error = await Assert.That(async () => await call.ResponseAsync).ThrowsExactly<SdkError>();
        await Assert.That(error!.Code).IsEqualTo(SdkErrorCode.Unauthenticated);
    }

    [Test]
    public async Task DownstreamRpcExceptionIsProjectedToSdkError()
    {
        var interceptor = new AuthInterceptor(new AuthOptions
        {
            Source = new StaticBearerTokenSource("t1"),
        });

        var context = new ClientInterceptorContext<string, string>(_method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(
            "req",
            context,
            (req, ctx) => FailingCall(new RpcException(new Status(StatusCode.Unavailable, "boom"))));

        var error = await Assert.That(async () => await call.ResponseAsync).ThrowsExactly<SdkError>();
        await Assert.That(error!.Code).IsEqualTo(SdkErrorCode.Unavailable);
    }

    [Test]
    public async Task ConstructorRejectsNullOptions()
    {
        await Assert.That(() => new AuthInterceptor(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    private static AsyncUnaryCall<string> ResponseCall(string body) =>
        new(
            Task.FromResult(body),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });

    private static AsyncUnaryCall<string> FailingCall(Exception failure) =>
        new(
            Task.FromException<string>(failure),
            Task.FromResult(new Metadata()),
            () => Status.DefaultCancelled,
            () => [],
            () => { });
}

internal sealed class FailingTokenSource : ITokenSource
{
    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken) =>
        ValueTask.FromException<string>(new InvalidOperationException("no idp"));
}
