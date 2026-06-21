using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Blessed Broker-mode source that POSTs to a localhost broker
/// endpoint and forwards the issued token on outgoing calls per
/// cross-SDK RFC 0019. Caches the token until the smaller of the
/// broker-supplied expires_in and the configured cap. Thread-safe;
/// one in-flight broker exchange at a time.
/// </summary>
public sealed class LocalEndpointBrokerSource : IBrokerSource, IDisposable
{
    /// <summary>
    /// Default upper bound on how long a broker token stays cached.
    /// RFC 0019 pins this at 30 seconds because the broker can
    /// rotate tokens out from under the SDK without warning.
    /// </summary>
    public static readonly TimeSpan DefaultMaxCacheDuration = TimeSpan.FromSeconds(30);

    private readonly LocalEndpointBrokerConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxCache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _cachedExpiry = DateTimeOffset.MinValue;

    public LocalEndpointBrokerSource(LocalEndpointBrokerConfig config)
        : this(config, TimeProvider.System)
    {
    }

    internal LocalEndpointBrokerSource(LocalEndpointBrokerConfig config, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ValidateEndpoint(config.Endpoint);
        _config = config;
        _timeProvider = timeProvider;
        _maxCache = config.MaxCacheDuration ?? DefaultMaxCacheDuration;
    }

    /// <inheritdoc />
    public string Origin => "local-endpoint";

    /// <inheritdoc />
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
            TokenResponse token = await ExchangeAsync(cancellationToken).ConfigureAwait(false);
            _cachedToken = token.AccessToken;
            TimeSpan ttl = token.ExpiresInSeconds is int seconds && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : _maxCache;
            if (ttl > _maxCache)
            {
                ttl = _maxCache;
            }
            _cachedExpiry = _timeProvider.GetUtcNow().Add(ttl);
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
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
        // No skew window. The Direct path uses skew to refresh before
        // the IdP's fixed expiry; broker tokens can rotate at any
        // moment so the cap is the freshness guarantee.
        return _cachedExpiry > _timeProvider.GetUtcNow();
    }

    private async Task<TokenResponse> ExchangeAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.Endpoint);

        var form = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(_config.Audience))
        {
            form["audience"] = _config.Audience!;
        }
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
        request.Content = new FormUrlEncodedContent(form);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _config.HttpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OAuthException(
                OAuthErrorCodes.BrokerUnavailable,
                $"broker endpoint unreachable: {ex.Message}",
                innerException: ex);
        }

        try
        {
            string body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            ThrowOnBrokerHttpFailure(response.StatusCode, body);
            try
            {
                return TokenResponse.ParseSuccess(body);
            }
            catch (Exception ex) when (ex is not OAuthException)
            {
                throw new OAuthException(
                    OAuthErrorCodes.BrokerProtocol,
                    $"broker response is not a valid token response: {ex.Message}",
                    innerException: ex);
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    private static void ThrowOnBrokerHttpFailure(HttpStatusCode status, string body)
    {
        int code = (int)status;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new OAuthException(
                OAuthErrorCodes.BrokerUnauthorised,
                $"broker rejected the request: HTTP {code}: {body}",
                httpStatusCode: code);
        }
        if (code is < 200 or >= 300)
        {
            throw new OAuthException(
                OAuthErrorCodes.BrokerUnavailable,
                $"broker returned HTTP {code}: {body}",
                httpStatusCode: code);
        }
    }

    private static void ValidateEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        bool isHttps = string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        if (isHttps || endpoint.IsLoopback)
        {
            return;
        }
        throw new ArgumentException(
            "Broker endpoint must use https or a loopback host.",
            nameof(endpoint));
    }
}
