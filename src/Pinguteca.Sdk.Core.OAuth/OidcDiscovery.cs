using System.Net.Http;
using System.Text.Json;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// RFC 8414 + OIDC Discovery 1.0 authorization-server metadata
/// fetcher. Pinned uncached at this layer per cross-SDK RFC 0017;
/// consumers wrap with their own cache when needed.
/// </summary>
public static class OidcDiscovery
{
    private const string WellKnownPath = ".well-known/openid-configuration";

    /// <summary>
    /// Fetch and validate the issuer's discovery document.
    /// </summary>
    /// <exception cref="OAuthException">
    /// HTTPS check fails, the HTTP response is non-success, the body
    /// is not valid JSON, required fields are missing, or the
    /// response <c>issuer</c> does not match the requested issuer
    /// (RFC 8414 section 3.3).
    /// </exception>
    public static async Task<OidcMetadata> DiscoverAsync(
        OidcDiscoveryConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Issuer.Scheme != Uri.UriSchemeHttps)
        {
            throw new OAuthException(
                OAuthErrorCodes.InvalidIssuer,
                $"Issuer must be HTTPS; got scheme '{config.Issuer.Scheme}'.");
        }

        Uri discoveryUrl = BuildDiscoveryUrl(config.Issuer);

        using HttpResponseMessage response = await config.HttpClient
            .GetAsync(discoveryUrl, cancellationToken)
            .ConfigureAwait(false);

        string body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthException(
                OAuthErrorCodes.InvalidIssuer,
                $"Discovery endpoint returned HTTP {(int)response.StatusCode}: {Truncate(body, 200)}",
                httpStatusCode: (int)response.StatusCode);
        }

        OidcMetadata metadata = ParseMetadata(body);

        if (!IssuersMatch(config.Issuer, metadata.Issuer))
        {
            throw new OAuthException(
                OAuthErrorCodes.InvalidIssuer,
                $"Discovery 'issuer' '{metadata.Issuer}' does not match requested '{config.Issuer}'.");
        }

        return metadata;
    }

    private static Uri BuildDiscoveryUrl(Uri issuer)
    {
        // RFC 8414 section 3: discovery URL is the issuer with
        // .well-known/openid-configuration appended. Issuers commonly
        // have a trailing slash and sometimes a path component; both
        // shapes are valid, and a naive concat double-slashes.
        string baseUrl = issuer.AbsoluteUri.TrimEnd('/');
        return new Uri($"{baseUrl}/{WellKnownPath}");
    }

    private static bool IssuersMatch(Uri requested, string returned)
    {
        // Byte-for-byte after a trailing-slash normalize. RFC 8414
        // section 3.3 prescribes exact string equality but real-world
        // servers wobble on the trailing slash.
        string normalizedReq = requested.AbsoluteUri.TrimEnd('/');
        string normalizedRet = (returned ?? string.Empty).TrimEnd('/');
        return string.Equals(normalizedReq, normalizedRet, StringComparison.Ordinal);
    }

    private static OidcMetadata ParseMetadata(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new OAuthException(
                OAuthErrorCodes.InvalidResponse,
                "Discovery body is not valid JSON.",
                innerException: ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OAuthException(
                    OAuthErrorCodes.InvalidResponse,
                    "Discovery body is not a JSON object.");
            }

            string issuer = RequireString(root, "issuer");
            Uri authzEndpoint = RequireAbsoluteUri(root, "authorization_endpoint");
            Uri tokenEndpoint = RequireAbsoluteUri(root, "token_endpoint");

            Uri? userInfoEndpoint = OptionalAbsoluteUri(root, "userinfo_endpoint");
            Uri? jwksUri = OptionalAbsoluteUri(root, "jwks_uri");

            IReadOnlyList<string> scopes = OptionalStringArray(root, "scopes_supported");
            IReadOnlyList<string> grantTypes = OptionalStringArray(root, "grant_types_supported");
            IReadOnlyList<string> authMethods = OptionalStringArray(root, "token_endpoint_auth_methods_supported");
            IReadOnlyList<string> pkceMethods = OptionalStringArray(root, "code_challenge_methods_supported");

            OidcMtlsEndpointAliases? mtlsAliases = ParseMtlsAliases(root);

            return new OidcMetadata(
                Issuer: issuer,
                AuthorizationEndpoint: authzEndpoint,
                TokenEndpoint: tokenEndpoint,
                UserInfoEndpoint: userInfoEndpoint,
                JwksUri: jwksUri,
                ScopesSupported: scopes,
                GrantTypesSupported: grantTypes,
                TokenEndpointAuthMethodsSupported: authMethods,
                CodeChallengeMethodsSupported: pkceMethods,
                MtlsEndpointAliases: mtlsAliases);
        }
    }

    private static OidcMtlsEndpointAliases? ParseMtlsAliases(JsonElement root)
    {
        if (!root.TryGetProperty("mtls_endpoint_aliases", out JsonElement aliases))
        {
            return null;
        }
        if (aliases.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return new OidcMtlsEndpointAliases(
            TokenEndpoint: OptionalAbsoluteUri(aliases, "token_endpoint"),
            RevocationEndpoint: OptionalAbsoluteUri(aliases, "revocation_endpoint"),
            IntrospectionEndpoint: OptionalAbsoluteUri(aliases, "introspection_endpoint"));
    }

    private static string RequireString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement el) || el.ValueKind != JsonValueKind.String)
        {
            throw new OAuthException(
                OAuthErrorCodes.InvalidResponse,
                $"Discovery body missing required string field '{property}'.");
        }
        string? value = el.GetString();
        if (string.IsNullOrEmpty(value))
        {
            throw new OAuthException(
                OAuthErrorCodes.InvalidResponse,
                $"Discovery body has empty required field '{property}'.");
        }
        return value;
    }

    private static Uri RequireAbsoluteUri(JsonElement root, string property)
    {
        string raw = RequireString(root, property);
        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
        {
            throw new OAuthException(
                OAuthErrorCodes.InvalidResponse,
                $"Discovery field '{property}' is not an absolute URI: '{raw}'.");
        }
        return uri;
    }

    private static Uri? OptionalAbsoluteUri(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        string? raw = el.GetString();
        return Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    private static IReadOnlyList<string> OptionalStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement el) || el.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        List<string> result = new(el.GetArrayLength());
        foreach (JsonElement item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? value = item.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(value);
                }
            }
        }
        return result;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}
