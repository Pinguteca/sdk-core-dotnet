using System;
using Grpc.Core;
using Pinguteca.Sdk.Core.Errors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests.Errors;

public sealed class SdkErrorTests
{
    [Test]
    public async Task FromRpcException_MapsStatusCode()
    {
        var rpc = new RpcException(new Status(StatusCode.Unavailable, "no upstream"));

        var err = SdkError.FromRpcException(rpc);

        await Assert.That(err.Code).IsEqualTo(SdkErrorCode.Unavailable);
        await Assert.That(err.Message).IsEqualTo("no upstream");
        await Assert.That(err.RpcSource).IsEqualTo(rpc);
    }

    [Test]
    public async Task FromRpcException_ExtractsRetryAfterFromTrailers()
    {
        var trailers = new Metadata
        {
            { "retry-after", "2.5" },
        };
        var rpc = new RpcException(new Status(StatusCode.Unavailable, "slow"), trailers);

        var err = SdkError.FromRpcException(rpc);

        await Assert.That(err.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(2.5));
    }

    [Test]
    public async Task FromRpcException_RetryAfterNullWhenAbsent()
    {
        var rpc = new RpcException(new Status(StatusCode.Internal, "boom"));

        var err = SdkError.FromRpcException(rpc);

        await Assert.That(err.RetryAfter).IsNull();
    }

    [Test]
    [Arguments(StatusCode.Unavailable, true)]
    [Arguments(StatusCode.DeadlineExceeded, true)]
    [Arguments(StatusCode.ResourceExhausted, true)]
    [Arguments(StatusCode.Aborted, true)]
    [Arguments(StatusCode.Internal, false)]
    [Arguments(StatusCode.NotFound, false)]
    [Arguments(StatusCode.PermissionDenied, false)]
    [Arguments(StatusCode.InvalidArgument, false)]
    public async Task IsRetryable_MatchesGenericRetryPolicy(StatusCode grpcCode, bool expected)
    {
        var rpc = new RpcException(new Status(grpcCode, "test"));

        var err = SdkError.FromRpcException(rpc);

        await Assert.That(err.IsRetryable).IsEqualTo(expected);
    }

    [Test]
    public async Task FromRpcException_NullArgumentThrows()
    {
        await Assert.That(() => SdkError.FromRpcException(null!))
            .ThrowsExactly<ArgumentNullException>();
    }
}
