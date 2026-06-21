using Pinguteca.Sdk.Core.Auth;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Contract every Broker-mode token source satisfies per cross-SDK
/// RFC 0019. Implementations MUST NOT run OIDC discovery, sign DPoP
/// proofs, present mTLS client certificates, or talk to the IdP
/// directly. They forward tokens issued upstream by the broker.
/// </summary>
public interface IBrokerSource : IRotatingTokenSource
{
    /// <summary>
    /// Stable label identifying the broker implementation, used in
    /// error messages and observability hooks.
    /// </summary>
    string Origin { get; }
}
