using Pinguteca.Sdk.Core.Auth;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// <see cref="IRotatingTokenSource"/> backed by an authorization_code
/// session. Constructed from the initial <see cref="TokenResponse"/>
/// the consumer obtained via <see cref="AuthorizationCodeFlow.ExchangeAsync"/>;
/// refreshes via the refresh token when the cached access token is
/// near expiry. Thread-safe; one in-flight refresh at a time.
/// </summary>
public sealed class AuthorizationCodeTokenSource : IRotatingTokenSource, IDisposable
{
    private readonly AuthorizationCodeFlow _flow;
    private readonly TimeSpan _refreshSkew;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string _accessToken;
    private DateTimeOffset _expiresAt;
    private string? _refreshToken;

    public AuthorizationCodeTokenSource(AuthorizationCodeFlow flow, TokenResponse initial)
        : this(flow, initial, utcNow: null)
    {
    }

    internal AuthorizationCodeTokenSource(
        AuthorizationCodeFlow flow,
        TokenResponse initial,
        Func<DateTimeOffset>? utcNow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(initial);
        if (string.IsNullOrEmpty(initial.AccessToken))
        {
            throw new ArgumentException(
                "Initial TokenResponse must carry a non-empty AccessToken.",
                nameof(initial));
        }

        _flow = flow;
        _refreshSkew = flow.RefreshSkew;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _accessToken = initial.AccessToken;
        _refreshToken = initial.RefreshToken;
        _expiresAt = initial.ExpiresInSeconds is int seconds
            ? _utcNow().AddSeconds(seconds)
            : DateTimeOffset.MaxValue;
    }

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsFresh())
        {
            return _accessToken;
        }
        if (_refreshToken is null)
        {
            // No refresh token issued; cached access token is the
            // only credential the source has. Return it and let the
            // caller's RotationInterceptor handle the eventual 401.
            return _accessToken;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsFresh() || _refreshToken is null)
            {
                return _accessToken;
            }

            TokenResponse refreshed = await _flow
                .RefreshAsync(_refreshToken, cancellationToken)
                .ConfigureAwait(false);

            _accessToken = refreshed.AccessToken;
            _expiresAt = refreshed.ExpiresInSeconds is int seconds
                ? _utcNow().AddSeconds(seconds)
                : DateTimeOffset.MaxValue;
            // Rotate the refresh token if the IdP returned a new one;
            // OAuth 2.0 Security BCP recommends rotation, and some
            // IdPs invalidate the prior refresh_token on use.
            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
            {
                _refreshToken = refreshed.RefreshToken;
            }
            return _accessToken;
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
            // Mark the cached access token expired so the next
            // GetTokenAsync hits the refresh path. We keep the
            // refresh token; rotation interceptor is meant to
            // recover from a stale access token, not a revoked
            // session.
            _expiresAt = DateTimeOffset.MinValue;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private bool IsFresh()
    {
        // Invalidate() sets _expiresAt to MinValue; subtracting the
        // skew from that would overflow, so short-circuit the
        // "definitely expired" case first.
        if (_expiresAt == DateTimeOffset.MinValue)
        {
            return false;
        }
        return _expiresAt - _refreshSkew > _utcNow();
    }
}
