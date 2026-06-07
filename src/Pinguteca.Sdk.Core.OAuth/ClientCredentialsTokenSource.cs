using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Pinguteca.Sdk.Core.Auth;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// <see cref="IRotatingTokenSource"/> backed by the OAuth 2.0
/// client_credentials grant per RFC 6749 section 4.4 and cross-SDK
/// RFC 0017. Caches the access token in memory and re-fetches a
/// fresh one slightly before expiry. Thread-safe; one in-flight
/// fetch at a time.
/// </summary>
public sealed class ClientCredentialsTokenSource : IRotatingTokenSource, IDisposable
{
    private readonly ClientCredentialsConfig _config;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _cachedExpiry = DateTimeOffset.MinValue;

    public ClientCredentialsTokenSource(ClientCredentialsConfig config)
        : this(config, utcNow: null)
    {
    }

    internal ClientCredentialsTokenSource(ClientCredentialsConfig config, Func<DateTimeOffset>? utcNow)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateAuthMode(config.AuthMode, config.ClientSecret);
        _config = config;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Resolve the IdP's token endpoint via OIDC discovery (RFC 8414)
    /// and build a token source pointing at it.
    /// </summary>
    public static async Task<ClientCredentialsTokenSource> FromIssuerAsync(
        ClientCredentialsFromIssuerConfig config,
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

        // RFC 8705 section 5: prefer the mTLS-bound alias endpoint when
        // the consumer authenticates with a client certificate. See
        // AuthorizationCodeFlow.FromIssuerAsync for the same routing.
        Uri tokenEndpoint = config.AuthMode == ClientAuthMode.Mtls
            && metadata.MtlsEndpointAliases?.TokenEndpoint is { } mtlsTokenEndpoint
                ? mtlsTokenEndpoint
                : metadata.TokenEndpoint;

        return new ClientCredentialsTokenSource(new ClientCredentialsConfig
        {
            TokenEndpoint = tokenEndpoint,
            ClientId = config.ClientId,
            ClientSecret = config.ClientSecret,
            Scopes = config.Scopes,
            AdditionalParameters = config.AdditionalParameters,
            HttpClient = config.HttpClient,
            RefreshSkew = config.RefreshSkew,
            AuthMode = config.AuthMode,
        });
    }

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsFresh())
        {
            return _cachedToken!;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsFresh())
            {
                return _cachedToken!;
            }

            TokenResponse token = await PostAsync(cancellationToken).ConfigureAwait(false);
            _cachedToken = token.AccessToken;
            _cachedExpiry = token.ExpiresInSeconds is int seconds
                ? _utcNow().AddSeconds(seconds)
                : DateTimeOffset.MaxValue;
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _cachedToken = null;
            _cachedExpiry = DateTimeOffset.MinValue;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private bool IsFresh()
    {
        if (_cachedToken is null || _cachedExpiry == DateTimeOffset.MinValue)
        {
            return false;
        }
        return _cachedExpiry - _config.RefreshSkew > _utcNow();
    }

    private async Task<TokenResponse> PostAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.TokenEndpoint);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        };
        if (_config.Scopes is { Count: > 0 } scopes)
        {
            form["scope"] = string.Join(' ', scopes);
        }
        if (_config.AdditionalParameters is { } extra)
        {
            foreach (KeyValuePair<string, string> pair in extra)
            {
                form[pair.Key] = pair.Value;
            }
        }

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
