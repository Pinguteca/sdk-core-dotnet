using System;

namespace Pinguteca.Sdk.Core.Mtls;

/// <summary>
/// Misconfiguration surface thrown by <see cref="MtlsHelper"/> at
/// construction time. Strict failure on misconfiguration is the
/// cross-SDK contract pinned in
/// <c>sdk-scaffold/docs/rfc/0014-mtls-helper.md</c>: missing files,
/// mismatched key, oversize file, non-PEM bytes, or
/// <c>InsecureSkipVerify = true</c> all return an error before any
/// connection is attempted.
/// </summary>
public sealed class MtlsException : Exception
{
    /// <summary>Sentinel code consumers can branch on.</summary>
    public MtlsErrorCode Code { get; }

    public MtlsException(MtlsErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public MtlsException(MtlsErrorCode code, string message, Exception inner)
        : base(message, inner)
    {
        Code = code;
    }
}
