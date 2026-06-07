using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Pinguteca.Sdk.Core.Auth;

/// <summary>
/// Parameterises an OAuth 2.0 client_credentials flow. No MSAL,
/// no IdentityModel; the request is a plain form-encoded POST to
/// <see cref="TokenUrl"/>. Works against any compliant IdP
/// (Keycloak, Auth0, Entra ID via OIDC, Cognito, custom).
/// </summary>
[Obsolete("Moved to Pinguteca.Sdk.Core.OAuth.ClientCredentialsConfig in the Pinguteca.Sdk.Core.OAuth package. Slated for removal one minor after the OAuth companion ships.")]
public sealed class ClientCredentialsOptions
{
    /// <summary>Token endpoint URL.</summary>
    public required Uri TokenUrl { get; init; }

    /// <summary>Client identifier registered at the IdP.</summary>
    public required string ClientId { get; init; }

    /// <summary>Client secret registered at the IdP.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Scopes to request. Encoded as a space-separated value of the
    /// form <c>scope</c> parameter when at least one is present.
    /// </summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>
    /// Selects how credentials are presented. The default uses
    /// HTTP Basic auth (Authorization: Basic ...) which most IdPs
    /// accept; <see cref="ClientAuthStyle.InBody"/> falls back to
    /// posting <c>client_id</c> and <c>client_secret</c> in the
    /// form body for IdPs that require it.
    /// </summary>
    public ClientAuthStyle AuthStyle { get; init; } = ClientAuthStyle.Basic;

    /// <summary>
    /// Extra parameters appended to the form body alongside
    /// <c>grant_type</c> and <c>scope</c>. Useful for IdPs that
    /// require an <c>audience</c> or <c>resource</c> parameter.
    /// </summary>
    public IReadOnlyDictionary<string, string>? AdditionalParameters { get; init; }

    /// <summary>
    /// HttpClient used to call the token endpoint. Default null
    /// means the source creates and owns a single
    /// <see cref="HttpClient"/>. Inject one via
    /// <c>IHttpClientFactory</c> in DI to share connection pools
    /// and resilience policies with the rest of the consumer's
    /// HTTP traffic.
    /// </summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>
    /// How early to refresh a token before its declared expiry.
    /// Default 60 seconds. Avoids the foot-gun where a token is
    /// served valid but expires mid-flight on the upstream call.
    /// </summary>
    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>Credential transport style for the token endpoint POST.</summary>
[Obsolete("Use Pinguteca.Sdk.Core.OAuth.ClientAuthMode instead (Basic / FormPost / None / Mtls). Slated for removal one minor after the OAuth companion ships.")]
public enum ClientAuthStyle
{
    /// <summary>HTTP Basic <c>Authorization</c> header.</summary>
    Basic = 0,

    /// <summary>Form body parameters <c>client_id</c> + <c>client_secret</c>.</summary>
    InBody = 1,
}
