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

public sealed class RotationInterceptorTests
{
    private static readonly Method<string, string> _method = new(
        MethodType.Unary,
        "svc.v1.Svc",
        "Do",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    [Test]
    public async Task Constructor_NullOptions_Throws()
    {
        await Assert.That(() => new RotationInterceptor(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullSource_Throws()
    {
        await Assert.That(() => new RotationInterceptor(new RotationOptions()))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Success_PassesThroughWithoutRotation()
    {
        var source = new RecordingSource();
        var interceptor = new RotationInterceptor(new RotationOptions { Source = source });
        var fake = new FakeContinuation(SuccessCall());

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(source.InvalidateCalls).IsEqualTo(0);
        await Assert.That(fake.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task FirstUnauthenticated_InvalidatesAndRetriesOnce()
    {
        var source = new RecordingSource();
        var interceptor = new RotationInterceptor(new RotationOptions { Source = source });
        var fake = new FakeContinuation(
            FailingCall(new Status(StatusCode.Unauthenticated, "expired")),
            SuccessCall());

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(source.InvalidateCalls).IsEqualTo(1);
        await Assert.That(fake.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task SecondUnauthenticated_DoesNotLoop()
    {
        var source = new RecordingSource();
        var interceptor = new RotationInterceptor(new RotationOptions { Source = source });
        var fake = new FakeContinuation(
            FailingCall(new Status(StatusCode.Unauthenticated, "bad")),
            FailingCall(new Status(StatusCode.Unauthenticated, "still bad")));

        await Assert.That(async () => await Invoke(interceptor, fake))
            .Throws<Exception>();

        await Assert.That(source.InvalidateCalls).IsEqualTo(1);
        await Assert.That(fake.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task PermissionDenied_DoesNotTriggerRotation()
    {
        var source = new RecordingSource();
        var interceptor = new RotationInterceptor(new RotationOptions { Source = source });
        var fake = new FakeContinuation(
            FailingCall(new Status(StatusCode.PermissionDenied, "forbidden")));

        await Assert.That(async () => await Invoke(interceptor, fake))
            .Throws<RpcException>();

        await Assert.That(source.InvalidateCalls).IsEqualTo(0);
        await Assert.That(fake.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task SdkErrorUnauthenticated_TriggersRotation()
    {
        var source = new RecordingSource();
        var interceptor = new RotationInterceptor(new RotationOptions { Source = source });
        var fake = new FakeContinuation(
            FailingCall(new SdkError(SdkErrorCode.Unauthenticated, "wrapped")),
            SuccessCall());

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(source.InvalidateCalls).IsEqualTo(1);
        await Assert.That(fake.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task NonIdempotent_WithGateOn_SkipsRotation()
    {
        var source = new RecordingSource();
        var interceptor = new RotationInterceptor(new RotationOptions
        {
            Source = source,
            IsIdempotent = _ => false,
        });
        var fake = new FakeContinuation(
            FailingCall(new Status(StatusCode.Unauthenticated, "expired")));

        await Assert.That(async () => await Invoke(interceptor, fake))
            .Throws<RpcException>();

        await Assert.That(source.InvalidateCalls).IsEqualTo(0);
        await Assert.That(fake.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task NullPredicate_DefaultsToRotate()
    {
        var source = new RecordingSource();
        var interceptor = new RotationInterceptor(new RotationOptions
        {
            Source = source,
            IsIdempotent = null,
        });
        var fake = new FakeContinuation(
            FailingCall(new Status(StatusCode.Unauthenticated, "expired")),
            SuccessCall());

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(source.InvalidateCalls).IsEqualTo(1);
    }

    [Test]
    public async Task AllowNonIdempotent_OverridesPredicate()
    {
        var source = new RecordingSource();
        var interceptor = new RotationInterceptor(new RotationOptions
        {
            Source = source,
            AllowNonIdempotent = true,
            IsIdempotent = _ => false,
        });
        var fake = new FakeContinuation(
            FailingCall(new Status(StatusCode.Unauthenticated, "expired")),
            SuccessCall());

        var result = await Invoke(interceptor, fake);

        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(source.InvalidateCalls).IsEqualTo(1);
    }

    // ---------- helpers ----------

    private static async Task<string> Invoke(RotationInterceptor interceptor, FakeContinuation fake)
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

    private static AsyncUnaryCall<string> FailingCall(Exception exception) =>
        new(
            Task.FromException<string>(exception),
            Task.FromResult(new Metadata()),
            () => Status.DefaultCancelled,
            () => [],
            () => { });
}

internal sealed class RecordingSource : IRotatingTokenSource
{
    public int InvalidateCalls { get; private set; }

    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken) =>
        new("test-token");

    public void Invalidate() => InvalidateCalls++;
}

internal sealed class FakeContinuation
{
    private readonly AsyncUnaryCall<string>[] _scripted;
    private int _index;

    public int Calls { get; private set; }

    public FakeContinuation(params AsyncUnaryCall<string>[] scripted)
    {
        _scripted = scripted;
    }

    public AsyncUnaryCall<string> Continuation(
        string request,
        ClientInterceptorContext<string, string> context)
    {
        Calls++;
        var call = _scripted[Math.Min(_index, _scripted.Length - 1)];
        _index++;
        return call;
    }
}
