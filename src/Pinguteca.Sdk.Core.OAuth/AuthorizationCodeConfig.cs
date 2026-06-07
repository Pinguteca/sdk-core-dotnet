using System.Net.Http;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Concrete inputs for <see cref="AuthorizationCodeFlow"/>. Used
/// when the caller already has the authorization and token
/// endpoints (e.g. from a static configuration or a prior OIDC
/// discovery the consumer cached themselves).
/// </summary>
/// <remarks>
/// For the discovery-driven path, see
/// <see cref="AuthorizationCodeFromIssuerConfig"/> and
/// <see cref="AuthorizationCodeFlow.FromIssuerAsync"/>.
/// </remarks>
public sealed class AuthorizationCodeConfig
{
    /// <summary>The IdP-registered client identifier.</summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Client secret. Required for <see cref="ClientAuthMode.Basic"/>
    /// and <see cref="ClientAuthMode.FormPost"/>; must be null for
    /// <see cref="ClientAuthMode.None"/> and
    /// <see cref="ClientAuthMode.Mtls"/>.
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>Authorization endpoint (where the user lands).</summary>
    public required Uri AuthorizationEndpoint { get; init; }

    /// <summary>Token endpoint (where exchange and refresh hit).</summary>
    public required Uri TokenEndpoint { get; init; }

    /// <summary>
    /// Exact-match redirect URI registered with the IdP. The flow
    /// echoes this value back to the IdP; the IdP enforces the
    /// match per RFC 6749 section 3.1.2.2.
    /// </summary>
    public required Uri RedirectUri { get; init; }

    /// <summary>OAuth 2.0 scopes; emitted space-separated when present.</summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>HTTP client used for token-endpoint requests.</summary>
    public required HttpClient HttpClient { get; init; }

    /// <summary>
    /// How long before the server-reported expiry the token source
    /// should consider the cached token stale and refresh proactively.
    /// Default 30s; absorbs typical clock-skew between client and
    /// server per cross-SDK RFC 0017.
    /// </summary>
    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Client authentication mode at the token endpoint.</summary>
    public ClientAuthMode AuthMode { get; init; } = ClientAuthMode.Basic;
}

/// <summary>
/// Discovery-driven variant: supply the issuer plus client and
/// transport options, and <see cref="AuthorizationCodeFlow.FromIssuerAsync"/>
/// resolves the authorization and token endpoints via OIDC
/// discovery (RFC 8414).
/// </summary>
public sealed class AuthorizationCodeFromIssuerConfig
{
    /// <summary>OIDC issuer URL. Must be HTTPS.</summary>
    public required Uri Issuer { get; init; }

    /// <inheritdoc cref="AuthorizationCodeConfig.ClientId"/>
    public required string ClientId { get; init; }

    /// <inheritdoc cref="AuthorizationCodeConfig.ClientSecret"/>
    public string? ClientSecret { get; init; }

    /// <inheritdoc cref="AuthorizationCodeConfig.RedirectUri"/>
    public required Uri RedirectUri { get; init; }

    /// <inheritdoc cref="AuthorizationCodeConfig.Scopes"/>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <inheritdoc cref="AuthorizationCodeConfig.HttpClient"/>
    public required HttpClient HttpClient { get; init; }

    /// <inheritdoc cref="AuthorizationCodeConfig.RefreshSkew"/>
    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc cref="AuthorizationCodeConfig.AuthMode"/>
    public ClientAuthMode AuthMode { get; init; } = ClientAuthMode.Basic;
}
