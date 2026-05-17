using System.Security.Authentication;

namespace Pinguteca.Sdk.Core.Mtls;

/// <summary>
/// Knobs for <see cref="MtlsHelper"/>. The zero value is the recommended
/// default per RFC 0014: TLS 1.3 minimum and
/// <see cref="InsecureSkipVerify"/> = false.
/// </summary>
public sealed class MtlsOptions
{
    /// <summary>
    /// Minimum negotiated TLS version. Defaults to TLS 1.3. Set to
    /// <see cref="SslProtocols.Tls12"/> only when talking to a server
    /// that cannot be upgraded; this loses forward-secrecy guarantees
    /// from 1.3.
    /// </summary>
    public SslProtocols MinVersion { get; init; } = SslProtocols.Tls13;

    /// <summary>
    /// Present so callers cannot avoid the check by surprise: setting
    /// it true causes the constructor to throw
    /// <see cref="MtlsException"/> with
    /// <see cref="MtlsErrorCode.InsecureSkipVerifyRejected"/>. Tests
    /// that genuinely need to skip verification should build their
    /// own <see cref="System.Net.Security.SslClientAuthenticationOptions"/>
    /// without going through this helper.
    /// </summary>
    public bool InsecureSkipVerify { get; init; }
}
