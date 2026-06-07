namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// How the client authenticates itself to the token endpoint when
/// trading an authorization code or refresh token for an access
/// token. Pinned in cross-SDK RFC 0017.
/// </summary>
public enum ClientAuthMode
{
    /// <summary>
    /// HTTP Basic authentication header carrying the client_id and
    /// client_secret per RFC 6749 section 2.3.1. Default whenever a
    /// client_secret is configured.
    /// </summary>
    Basic,

    /// <summary>
    /// client_id and client_secret in the form-encoded body, also
    /// per RFC 6749 section 2.3.1. Used for IdPs that reject the
    /// Basic header form.
    /// </summary>
    FormPost,

    /// <summary>
    /// Public client: no secret. Only the client_id is sent in the
    /// body. The flow MUST rely on PKCE to bind the authorization
    /// code to this client (RFC 7636).
    /// </summary>
    None,

    /// <summary>
    /// mTLS client authentication per RFC 8705 section 2. The
    /// client certificate at the TLS layer authenticates the
    /// request; no secret is sent in the HTTP payload. Selecting
    /// this mode requires an mTLS-configured <see cref="System.Net.Http.HttpClient"/>;
    /// the OAuth package surfaces a construction-time error when
    /// the HttpClient cannot present a client certificate (for
    /// example, on browser runtimes).
    /// </summary>
    Mtls,
}
