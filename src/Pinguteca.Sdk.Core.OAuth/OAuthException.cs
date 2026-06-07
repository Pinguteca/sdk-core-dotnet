using System.Text.Json;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Typed boundary for every failure inside the OAuth companion.
/// Carries the RFC 6749 section 5.2 error code, the human-readable
/// description, the optional <c>error_uri</c>, and the HTTP status
/// when the failure was an HTTP response. SDK-local error codes
/// (issuer validation, PKCE input checks, response shape) reuse
/// this type with codes prefixed by <c>invalid_</c>.
/// </summary>
public sealed class OAuthException : Exception
{
    /// <summary>RFC 6749 section 5.2 error code, or an SDK-local equivalent.</summary>
    public string ErrorCode { get; }

    /// <summary>Optional <c>error_description</c> populated by the server or the SDK.</summary>
    public string? ErrorDescription { get; }

    /// <summary>Optional <c>error_uri</c> the IdP supplied for further reading.</summary>
    public Uri? ErrorUri { get; }

    /// <summary>HTTP status code when this exception wraps a token endpoint response.</summary>
    public int? HttpStatusCode { get; }

    public OAuthException(
        string errorCode,
        string? errorDescription = null,
        Uri? errorUri = null,
        int? httpStatusCode = null,
        Exception? innerException = null)
        : base(BuildMessage(errorCode, errorDescription), innerException)
    {
        ArgumentException.ThrowIfNullOrEmpty(errorCode);
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
        ErrorUri = errorUri;
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// Build an exception from a non-2xx token endpoint response. The
    /// body is parsed per RFC 6749 section 5.2; if the body is not
    /// JSON or lacks an <c>error</c> field, the exception falls back
    /// to <see cref="OAuthErrorCodes.ServerError"/> with the raw body
    /// as the description.
    /// </summary>
    public static OAuthException FromTokenEndpointError(int httpStatusCode, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new OAuthException(
                OAuthErrorCodes.ServerError,
                $"Token endpoint returned HTTP {httpStatusCode} with an empty body.",
                httpStatusCode: httpStatusCode);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new OAuthException(
                    OAuthErrorCodes.ServerError,
                    body,
                    httpStatusCode: httpStatusCode);
            }

            string errorCode = root.TryGetProperty("error", out JsonElement err) && err.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(err.GetString())
                ? err.GetString()!
                : OAuthErrorCodes.ServerError;
            string? description = root.TryGetProperty("error_description", out JsonElement desc) && desc.ValueKind == JsonValueKind.String
                ? desc.GetString()
                : null;
            Uri? uri = root.TryGetProperty("error_uri", out JsonElement uriProp) && uriProp.ValueKind == JsonValueKind.String && Uri.TryCreate(uriProp.GetString(), UriKind.Absolute, out Uri? parsed)
                ? parsed
                : null;
            return new OAuthException(errorCode, description, uri, httpStatusCode);
        }
        catch (JsonException)
        {
            return new OAuthException(
                OAuthErrorCodes.ServerError,
                body,
                httpStatusCode: httpStatusCode);
        }
    }

    private static string BuildMessage(string errorCode, string? errorDescription)
        => string.IsNullOrEmpty(errorDescription)
            ? $"OAuth error: {errorCode}"
            : $"OAuth error: {errorCode}. {errorDescription}";
}

/// <summary>
/// Constants for the OAuth 2.0 error codes the companion emits.
/// RFC 6749 section 5.2 codes plus SDK-local ones for input checks
/// the IdP cannot have caused.
/// </summary>
public static class OAuthErrorCodes
{
    // RFC 6749 section 5.2.
    public const string InvalidRequest = "invalid_request";
    public const string InvalidClient = "invalid_client";
    public const string InvalidGrant = "invalid_grant";
    public const string UnauthorizedClient = "unauthorized_client";
    public const string UnsupportedGrantType = "unsupported_grant_type";
    public const string InvalidScope = "invalid_scope";

    // SDK-local codes for failures that the IdP did not cause.
    public const string InvalidResponse = "invalid_response";
    public const string InvalidIssuer = "invalid_issuer";
    public const string InvalidVerifier = "invalid_verifier";

    // Fallback when the IdP returns a body without a recognizable error field.
    public const string ServerError = "server_error";
}
