namespace Pinguteca.Sdk.Core.Errors;

/// <summary>
/// Canonical error codes the SDK surfaces. Mirrors the gRPC
/// <see cref="Grpc.Core.StatusCode"/> enum one-for-one so consumers
/// can switch on a single typed value without taking a direct
/// dependency on Grpc.Core. The numeric values match gRPC for
/// straight casting where needed.
/// </summary>
public enum SdkErrorCode
{
    /// <summary>Not an error. Reserved; the SDK never surfaces this.</summary>
    Ok = 0,
    Cancelled = 1,
    Unknown = 2,
    InvalidArgument = 3,
    DeadlineExceeded = 4,
    NotFound = 5,
    AlreadyExists = 6,
    PermissionDenied = 7,
    ResourceExhausted = 8,
    FailedPrecondition = 9,
    Aborted = 10,
    OutOfRange = 11,
    Unimplemented = 12,
    Internal = 13,
    Unavailable = 14,
    DataLoss = 15,
    Unauthenticated = 16,
}
