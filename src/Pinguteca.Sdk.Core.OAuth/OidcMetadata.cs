namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Parsed OpenID Connect / OAuth 2.0 authorization server metadata
/// document (RFC 8414 + OIDC Discovery 1.0).
/// </summary>
/// <param name="Issuer">
/// Issuer identifier as reported by the server. Compared byte-for-
/// byte against the requested issuer per RFC 8414 section 3.3;
/// discovery rejects responses whose issuer differs.
/// </param>
/// <param name="AuthorizationEndpoint">Authorization endpoint for the authorization code flow.</param>
/// <param name="TokenEndpoint">Token endpoint for all grants.</param>
/// <param name="UserInfoEndpoint">OIDC UserInfo endpoint; null when the server is OAuth-only.</param>
/// <param name="JwksUri">JWKS endpoint for ID-token signature validation.</param>
/// <param name="ScopesSupported">Scopes the server advertises; empty list when the field is absent.</param>
/// <param name="GrantTypesSupported">
/// Grant types the server supports. Empty when the field is absent;
/// RFC 6749 section 8.4 implies <c>authorization_code</c> and
/// <c>implicit</c> as defaults, but consumers should not rely on the
/// implication.
/// </param>
/// <param name="TokenEndpointAuthMethodsSupported">
/// Token endpoint client authentication methods. Consumers compare
/// against the configured <see cref="ClientAuthMode"/> equivalent
/// before issuing a token request.
/// </param>
/// <param name="CodeChallengeMethodsSupported">
/// PKCE challenge methods the server supports. Per RFC 0017 every
/// SDK pins <c>S256</c>; absence of <c>S256</c> here is a hard error
/// at the caller's discretion.
/// </param>
/// <param name="MtlsEndpointAliases">
/// Separate endpoint aliases the server exposes for mTLS-bound
/// requests (RFC 8705 section 5). Null when the server does not
/// advertise the field.
/// </param>
public sealed record OidcMetadata(
    string Issuer,
    Uri AuthorizationEndpoint,
    Uri TokenEndpoint,
    Uri? UserInfoEndpoint,
    Uri? JwksUri,
    IReadOnlyList<string> ScopesSupported,
    IReadOnlyList<string> GrantTypesSupported,
    IReadOnlyList<string> TokenEndpointAuthMethodsSupported,
    IReadOnlyList<string> CodeChallengeMethodsSupported,
    OidcMtlsEndpointAliases? MtlsEndpointAliases);

/// <summary>
/// Endpoint aliases the server advertises for mTLS-bound flows per
/// RFC 8705 section 5. When present, mTLS clients hit these URLs
/// rather than the top-level <see cref="OidcMetadata.TokenEndpoint"/>.
/// </summary>
public sealed record OidcMtlsEndpointAliases(
    Uri? TokenEndpoint,
    Uri? RevocationEndpoint,
    Uri? IntrospectionEndpoint);
