using System.Net.Http;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Inputs for an OAuth 2.0 client_credentials flow per cross-SDK
/// RFC 0017. Plain form-encoded POST to <see cref="TokenEndpoint"/>;
/// works against any compliant IdP (Keycloak, Auth0, Entra ID via
/// OIDC, Cognito, custom).
/// </summary>
public sealed class ClientCredentialsConfig
{
    /// <summary>Token endpoint URL.</summary>
    public required Uri TokenEndpoint { get; init; }

    /// <summary>Client identifier registered at the IdP.</summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Client secret. Required for <see cref="ClientAuthMode.Basic"/>
    /// and <see cref="ClientAuthMode.FormPost"/>; must be null for
    /// <see cref="ClientAuthMode.None"/> and
    /// <see cref="ClientAuthMode.Mtls"/>.
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Scopes to request. Encoded as a space-separated value of the
    /// form <c>scope</c> parameter when at least one is present.
    /// </summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>
    /// Extra parameters appended to the form body alongside
    /// <c>grant_type</c> and <c>scope</c>. Useful for IdPs that
    /// require an <c>audience</c> or <c>resource</c> parameter.
    /// </summary>
    public IReadOnlyDictionary<string, string>? AdditionalParameters { get; init; }

    /// <summary>HTTP client used to call the token endpoint.</summary>
    public required HttpClient HttpClient { get; init; }

    /// <summary>
    /// How early to refresh a token before its declared expiry.
    /// Default 30 seconds, matching the cross-SDK RFC 0017 skew
    /// guidance. Bumps the previous Sdk.Core/Auth shape's 60s
    /// default down to align with the rest of the SDK family.
    /// </summary>
    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Client authentication mode at the token endpoint.</summary>
    public ClientAuthMode AuthMode { get; init; } = ClientAuthMode.Basic;
}

/// <summary>
/// Discovery-driven variant of <see cref="ClientCredentialsConfig"/>.
/// <see cref="ClientCredentialsTokenSource.FromIssuerAsync"/> runs
/// OIDC discovery against the issuer and uses the returned
/// <c>token_endpoint</c>.
/// </summary>
public sealed class ClientCredentialsFromIssuerConfig
{
    /// <summary>OIDC issuer URL. Must be HTTPS.</summary>
    public required Uri Issuer { get; init; }

    /// <inheritdoc cref="ClientCredentialsConfig.ClientId"/>
    public required string ClientId { get; init; }

    /// <inheritdoc cref="ClientCredentialsConfig.ClientSecret"/>
    public string? ClientSecret { get; init; }

    /// <inheritdoc cref="ClientCredentialsConfig.Scopes"/>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <inheritdoc cref="ClientCredentialsConfig.AdditionalParameters"/>
    public IReadOnlyDictionary<string, string>? AdditionalParameters { get; init; }

    /// <inheritdoc cref="ClientCredentialsConfig.HttpClient"/>
    public required HttpClient HttpClient { get; init; }

    /// <inheritdoc cref="ClientCredentialsConfig.RefreshSkew"/>
    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc cref="ClientCredentialsConfig.AuthMode"/>
    public ClientAuthMode AuthMode { get; init; } = ClientAuthMode.Basic;
}
