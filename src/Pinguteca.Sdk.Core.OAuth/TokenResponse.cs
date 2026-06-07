using System.Text.Json;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Parsed RFC 6749 section 5.1 token endpoint success response,
/// with the OpenID Connect <c>id_token</c> tail when present.
/// </summary>
/// <param name="AccessToken">The opaque bearer token.</param>
/// <param name="TokenType">Token type; expected <c>Bearer</c>.</param>
/// <param name="ExpiresInSeconds">
/// Lifetime in seconds as reported by the server. Null when the IdP
/// omits the field; consumers should treat that as "long-lived" and
/// rely on <see cref="OAuthException"/> with <c>invalid_grant</c> to
/// detect expiry.
/// </param>
/// <param name="RefreshToken">Refresh token; populated only when the IdP returns one.</param>
/// <param name="Scope">Space-separated granted scopes; null if the IdP echoes nothing.</param>
/// <param name="IdToken">Signed ID Token for OIDC flows; null otherwise.</param>
public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int? ExpiresInSeconds,
    string? RefreshToken,
    string? Scope,
    string? IdToken)
{
    /// <summary>
    /// Parse the JSON body returned by a successful 2xx token
    /// endpoint response. Throws <see cref="OAuthException"/> with
    /// <c>invalid_response</c> when the body is malformed or lacks
    /// the required <c>access_token</c> / <c>token_type</c> fields.
    /// </summary>
    public static TokenResponse ParseSuccess(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new OAuthException(
                OAuthErrorCodes.InvalidResponse,
                "Token endpoint returned a body that is not valid JSON.",
                innerException: ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OAuthException(
                    OAuthErrorCodes.InvalidResponse,
                    "Token endpoint body is not a JSON object.");
            }

            string? accessToken = root.TryGetProperty("access_token", out JsonElement at) && at.ValueKind == JsonValueKind.String
                ? at.GetString()
                : null;
            string? tokenType = root.TryGetProperty("token_type", out JsonElement tt) && tt.ValueKind == JsonValueKind.String
                ? tt.GetString()
                : null;

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(tokenType))
            {
                throw new OAuthException(
                    OAuthErrorCodes.InvalidResponse,
                    "Token endpoint response missing required access_token or token_type.");
            }

            int? expiresIn = root.TryGetProperty("expires_in", out JsonElement ex) && ex.ValueKind == JsonValueKind.Number && ex.TryGetInt32(out int e)
                ? e
                : null;
            string? refreshToken = root.TryGetProperty("refresh_token", out JsonElement rt) && rt.ValueKind == JsonValueKind.String
                ? rt.GetString()
                : null;
            string? scope = root.TryGetProperty("scope", out JsonElement sc) && sc.ValueKind == JsonValueKind.String
                ? sc.GetString()
                : null;
            string? idToken = root.TryGetProperty("id_token", out JsonElement id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;

            return new TokenResponse(accessToken, tokenType, expiresIn, refreshToken, scope, idToken);
        }
    }
}
