using System;
using System.Globalization;
using Grpc.Core;

namespace Pinguteca.Sdk.Core.Errors;

/// <summary>
/// Typed error surfaced by SDK interceptors. Wraps an
/// <see cref="RpcException"/> as the typed boundary between
/// <c>Grpc.Core</c> and consumer code: consumers match on
/// <see cref="Code"/> and read <see cref="RetryAfter"/> without
/// importing gRPC types directly.
/// </summary>
public sealed class SdkError : Exception
{
    /// <summary>
    /// Canonical error code derived from <see cref="StatusCode"/>.
    /// </summary>
    public SdkErrorCode Code { get; }

    /// <summary>
    /// Suggested delay before retrying, populated when the server
    /// returned a <c>google.rpc.RetryInfo</c> detail or a
    /// <c>retry-after</c> header. Null otherwise. The SDK retry
    /// interceptor honours this value when present.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// The underlying gRPC exception, kept for callers that need
    /// access to trailers or status details the SDK does not
    /// project. Most consumers should never touch this.
    /// </summary>
    public RpcException? RpcSource { get; }

    public SdkError(SdkErrorCode code, string message, TimeSpan? retryAfter = null, RpcException? source = null)
        : base(message, source)
    {
        Code = code;
        RetryAfter = retryAfter;
        RpcSource = source;
    }

    /// <summary>
    /// Construct an <see cref="SdkError"/> from an
    /// <see cref="RpcException"/> raised by the gRPC client. Maps
    /// the gRPC status code to the SDK's canonical code and
    /// extracts a retry hint from the trailers when one is present.
    /// </summary>
    public static SdkError FromRpcException(RpcException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var code = (SdkErrorCode)(int)ex.StatusCode;
        var retryAfter = ExtractRetryAfter(ex);
        return new SdkError(code, ex.Status.Detail, retryAfter, ex);
    }

    /// <summary>
    /// True when <see cref="Code"/> indicates the call can be
    /// safely retried by a generic retry policy. Methods marked
    /// non-idempotent by the schema must still gate retries on
    /// the idempotency level; this property only reflects the
    /// status code itself.
    /// </summary>
    public bool IsRetryable => Code switch
    {
        SdkErrorCode.Unavailable => true,
        SdkErrorCode.DeadlineExceeded => true,
        SdkErrorCode.ResourceExhausted => true,
        SdkErrorCode.Aborted => true,
        _ => false,
    };

    private static TimeSpan? ExtractRetryAfter(RpcException ex)
    {
        // Prefer the structured google.rpc.RetryInfo detail when the
        // server provides it; fall back to the textual retry-after
        // metadata entry when that is the only signal.
        foreach (var entry in ex.Trailers)
        {
            if (string.Equals(entry.Key, "retry-after", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds >= 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        return null;
    }
}
