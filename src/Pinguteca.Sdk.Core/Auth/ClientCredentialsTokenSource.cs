using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Core.Auth;

/// <summary>
/// <see cref="ITokenSource"/> backed by OAuth 2.0
/// client_credentials. Caches the most recent token in memory and
/// refreshes it slightly before expiry. Thread-safe; one in-flight
/// fetch at a time across concurrent callers.
///
/// Implements <see cref="IRotatingTokenSource"/> so a future
/// rotation interceptor can invalidate the cache on a server-side
/// <c>Unauthenticated</c>.
/// </summary>
[Obsolete("Moved to Pinguteca.Sdk.Core.OAuth.ClientCredentialsTokenSource in the Pinguteca.Sdk.Core.OAuth package. Slated for removal one minor after the OAuth companion ships.")]
public sealed class ClientCredentialsTokenSource : IRotatingTokenSource, IDisposable
{
    private readonly ClientCredentialsOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _cachedExpiry = DateTimeOffset.MinValue;

    public ClientCredentialsTokenSource(ClientCredentialsOptions options)
        : this(options, utcNow: null)
    {
    }

    internal ClientCredentialsTokenSource(ClientCredentialsOptions options, Func<DateTimeOffset>? utcNow)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.ClientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(options));
        }
        if (string.IsNullOrEmpty(options.ClientSecret))
        {
            throw new ArgumentException("ClientSecret is required.", nameof(options));
        }
        _options = options;
        if (options.HttpClient is { } supplied)
        {
            _http = supplied;
            _ownsHttp = false;
        }
        else
        {
            _http = new HttpClient();
            _ownsHttp = true;
        }
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var now = _utcNow();
        if (_cachedToken is { } cached && _cachedExpiry - _options.RefreshSkew > now)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the gate; a concurrent caller may
            // have just populated the cache.
            now = _utcNow();
            if (_cachedToken is { } reCached && _cachedExpiry - _options.RefreshSkew > now)
            {
                return reCached;
            }

            var token = await FetchAsync(cancellationToken).ConfigureAwait(false);
            _cachedToken = token.AccessToken;
            _cachedExpiry = _utcNow().AddSeconds(token.ExpiresInSeconds);
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

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
        _gate.Dispose();
    }

    private async Task<TokenResponse> FetchAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        };
        if (_options.Scopes is { Count: > 0 } scopes)
        {
            form["scope"] = string.Join(' ', scopes);
        }
        if (_options.AdditionalParameters is { } extra)
        {
            foreach (var pair in extra)
            {
                form[pair.Key] = pair.Value;
            }
        }
        if (_options.AuthStyle == ClientAuthStyle.Basic)
        {
            var raw = $"{_options.ClientId}:{_options.ClientSecret}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
        else
        {
            form["client_id"] = _options.ClientId;
            form["client_secret"] = _options.ClientSecret;
        }

        request.Content = new FormUrlEncodedContent(form);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new TokenEndpointException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "auth: token endpoint returned {0}: {1}",
                    (int)response.StatusCode,
                    body));
        }

        var token = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            .ConfigureAwait(false);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new TokenEndpointException("auth: token endpoint returned an empty access_token.");
        }
        return token;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; set; } = 3600;

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}

/// <summary>
/// Thrown when the OAuth 2.0 token endpoint returns a non-2xx
/// response or a body without an <c>access_token</c>. The auth
/// interceptor catches this and surfaces an Unauthenticated
/// <see cref="Errors.SdkError"/> to the caller.
/// </summary>
[Obsolete("Use Pinguteca.Sdk.Core.OAuth.OAuthException (FromTokenEndpointError) instead. Slated for removal one minor after the OAuth companion ships.")]
public sealed class TokenEndpointException : Exception
{
    public TokenEndpointException(string message) : base(message) { }
    public TokenEndpointException(string message, Exception inner) : base(message, inner) { }
}
