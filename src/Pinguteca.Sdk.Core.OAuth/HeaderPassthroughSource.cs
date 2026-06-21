namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Broker-mode source that forwards a bound token an upstream
/// broker (typically a reverse proxy) attached to the consumer's
/// incoming request per cross-SDK RFC 0019. Use
/// <see cref="SetToken"/> from the consumer's request middleware
/// to update the cached value as each inbound request arrives.
/// The source MUST NOT contact any IdP and performs no caching
/// policy of its own; the broker upstream owns rotation.
/// </summary>
public sealed class HeaderPassthroughSource : IBrokerSource
{
    private readonly Lock _lock = new();
    private string? _token;

    /// <summary>
    /// Construct an unbound source. Call <see cref="SetToken"/>
    /// before <see cref="GetTokenAsync"/> or accept the resulting
    /// <see cref="OAuthException"/> with
    /// <see cref="OAuthErrorCodes.BrokerUnauthorised"/>.
    /// </summary>
    public HeaderPassthroughSource()
    {
    }

    /// <summary>Construct a source seeded with the supplied token.</summary>
    public HeaderPassthroughSource(string? token)
    {
        _token = string.IsNullOrEmpty(token) ? null : token;
    }

    /// <inheritdoc />
    public string Origin => "header-passthrough";

    /// <inheritdoc />
    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? snapshot;
        lock (_lock)
        {
            snapshot = _token;
        }
        if (string.IsNullOrEmpty(snapshot))
        {
            throw new OAuthException(
                OAuthErrorCodes.BrokerUnauthorised,
                "no broker-supplied token; expecting upstream Authorization header");
        }
        return ValueTask.FromResult(snapshot);
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        lock (_lock)
        {
            _token = null;
        }
    }

    /// <summary>
    /// Replace the held bound token. Call this from the consumer's
    /// HTTP middleware as each inbound request arrives with a fresh
    /// broker-issued token.
    /// </summary>
    public void SetToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        lock (_lock)
        {
            _token = token;
        }
    }
}
