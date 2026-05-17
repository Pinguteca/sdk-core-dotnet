namespace Pinguteca.Sdk.Core.Mtls;

/// <summary>
/// Sentinel codes for <see cref="MtlsException"/>. Consumers branch
/// on these instead of string-matching the message.
/// </summary>
public enum MtlsErrorCode
{
    /// <summary><c>InsecureSkipVerify = true</c> at construction.</summary>
    InsecureSkipVerifyRejected = 1,

    /// <summary>Required cert path was null or empty.</summary>
    EmptyCertPath = 2,

    /// <summary>Required key path was null or empty.</summary>
    EmptyKeyPath = 3,

    /// <summary>Required PKCS#12 path was null or empty.</summary>
    EmptyP12Path = 4,

    /// <summary>CA file contained no PEM certificate blocks.</summary>
    NoCAInFile = 5,

    /// <summary>File exceeded <see cref="MtlsHelper.MaxCertFileSize"/>.</summary>
    CertFileTooLarge = 6,

    /// <summary>File did not begin with the PEM header magic.</summary>
    InvalidPEM = 7,

    /// <summary>Certificate / key pair failed to load.</summary>
    LoadFailed = 8,
}
