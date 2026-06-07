using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// OAuth 2.0 authorization_code grant flow per RFC 6749 section 4.1,
/// with mandatory PKCE S256 per cross-SDK RFC 0017. Stateless; the
/// authorization URL is built client-side, the exchange and refresh
/// hit the token endpoint.
/// </summary>
public sealed class AuthorizationCodeFlow
{
    private readonly AuthorizationCodeConfig _config;

    /// <summary>
    /// Construct against explicit endpoints. For the discovery-driven
    /// path, call <see cref="FromIssuerAsync"/> instead.
    /// </summary>
    public AuthorizationCodeFlow(AuthorizationCodeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateAuthMode(config.AuthMode, config.ClientSecret);
        _config = config;
    }

    /// <summary>
    /// Resolve the IdP's authorization and token endpoints via OIDC
    /// discovery (RFC 8414), then construct the flow against them.
    /// </summary>
    public static async Task<AuthorizationCodeFlow> FromIssuerAsync(
        AuthorizationCodeFromIssuerConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        OidcMetadata metadata = await OidcDiscovery.DiscoverAsync(
            new OidcDiscoveryConfig
            {
                Issuer = config.Issuer,
                HttpClient = config.HttpClient,
            },
            cancellationToken).ConfigureAwait(false);

        // RFC 8705 section 5: when the server advertises mtls_endpoint_aliases
        // and the consumer selected mTLS authentication, the token request
        // must hit the alias endpoint (the regular one will not be configured
        // to require a client certificate). Authorization endpoint stays on
        // the top-level URL because it is a browser-side redirect; only the
        // back-channel token exchange is bound to the client cert.
        Uri tokenEndpoint = config.AuthMode == ClientAuthMode.Mtls
            && metadata.MtlsEndpointAliases?.TokenEndpoint is { } mtlsTokenEndpoint
                ? mtlsTokenEndpoint
                : metadata.TokenEndpoint;

        return new AuthorizationCodeFlow(new AuthorizationCodeConfig
        {
            ClientId = config.ClientId,
            ClientSecret = config.ClientSecret,
            AuthorizationEndpoint = metadata.AuthorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            RedirectUri = config.RedirectUri,
            Scopes = config.Scopes,
            HttpClient = config.HttpClient,
            RefreshSkew = config.RefreshSkew,
            AuthMode = config.AuthMode,
        });
    }

    /// <summary>
    /// Compose the authorization URL the consumer redirects the user
    /// to. The <paramref name="state"/> binds the response to the
    /// consumer's session and is mandatory per RFC 0017; the SDK
    /// rejects empty values rather than generating one because state
    /// binds to consumer-side state the SDK cannot see.
    /// </summary>
    public Uri BuildAuthorizationUrl(string state, PkcePair pkce)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);
        ArgumentNullException.ThrowIfNull(pkce);

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["response_type"] = "code";
        query["client_id"] = _config.ClientId;
        query["redirect_uri"] = _config.RedirectUri.AbsoluteUri;
        if (_config.Scopes is { Count: > 0 } scopes)
        {
            query["scope"] = string.Join(' ', scopes);
        }
        query["state"] = state;
        query["code_challenge"] = pkce.Challenge;
        query["code_challenge_method"] = PkcePair.ChallengeMethod;

        var builder = new UriBuilder(_config.AuthorizationEndpoint);
        // Preserve any pre-existing query on the authorization endpoint
        // (some IdPs add tenant or audience parameters there).
        if (!string.IsNullOrEmpty(builder.Query))
        {
            var existing = HttpUtility.ParseQueryString(builder.Query.TrimStart('?'));
            foreach (string? key in existing.AllKeys)
            {
                if (key is not null && query[key] is null)
                {
                    query[key] = existing[key];
                }
            }
        }
        builder.Query = query.ToString() ?? string.Empty;
        return builder.Uri;
    }

    /// <summary>
    /// Exchange an authorization code for an access token. The
    /// <paramref name="verifier"/> must be the PKCE verifier that
    /// produced the challenge sent in
    /// <see cref="BuildAuthorizationUrl"/>.
    /// </summary>
    public Task<TokenResponse> ExchangeAsync(
        string code,
        string verifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(verifier);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _config.RedirectUri.AbsoluteUri,
            ["code_verifier"] = verifier,
        };
        return PostTokenEndpointAsync(form, cancellationToken);
    }

    /// <summary>
    /// Exchange a refresh token for a new access token.
    /// </summary>
    public Task<TokenResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        if (_config.Scopes is { Count: > 0 } scopes)
        {
            form["scope"] = string.Join(' ', scopes);
        }
        return PostTokenEndpointAsync(form, cancellationToken);
    }

    /// <summary>Exposed so <see cref="AuthorizationCodeTokenSource"/> can read it.</summary>
    internal TimeSpan RefreshSkew => _config.RefreshSkew;

    private async Task<TokenResponse> PostTokenEndpointAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.TokenEndpoint);

        switch (_config.AuthMode)
        {
            case ClientAuthMode.Basic:
                {
                    string raw = $"{_config.ClientId}:{_config.ClientSecret}";
                    string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                    break;
                }
            case ClientAuthMode.FormPost:
                form["client_id"] = _config.ClientId;
                form["client_secret"] = _config.ClientSecret!;
                break;
            case ClientAuthMode.None:
            case ClientAuthMode.Mtls:
                form["client_id"] = _config.ClientId;
                break;
            default:
                throw new InvalidOperationException($"Unhandled ClientAuthMode '{_config.AuthMode}'.");
        }

        request.Content = new FormUrlEncodedContent(form);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await _config.HttpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        string body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw OAuthException.FromTokenEndpointError((int)response.StatusCode, body);
        }

        return TokenResponse.ParseSuccess(body);
    }

    private static void ValidateAuthMode(ClientAuthMode mode, string? clientSecret)
    {
        bool needsSecret = mode is ClientAuthMode.Basic or ClientAuthMode.FormPost;
        bool hasSecret = !string.IsNullOrEmpty(clientSecret);
        if (needsSecret && !hasSecret)
        {
            throw new ArgumentException(
                $"ClientAuthMode.{mode} requires a non-empty ClientSecret.",
                nameof(clientSecret));
        }
        if (!needsSecret && hasSecret)
        {
            throw new ArgumentException(
                $"ClientAuthMode.{mode} forbids a ClientSecret; remove it from the config.",
                nameof(clientSecret));
        }
    }
}
