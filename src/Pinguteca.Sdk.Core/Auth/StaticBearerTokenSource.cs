using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Core.Auth;

/// <summary>
/// <see cref="ITokenSource"/> that always yields the same token.
/// Useful for short-lived CI credentials or hand-issued service
/// tokens. Does not implement <see cref="IRotatingTokenSource"/>:
/// rotating a static token is a configuration error, not a
/// runtime recovery.
/// </summary>
public sealed class StaticBearerTokenSource : ITokenSource
{
    private readonly string _token;

    public StaticBearerTokenSource(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("Token must be non-empty.", nameof(token));
        }
        _token = token;
    }

    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken) => new(_token);
}
