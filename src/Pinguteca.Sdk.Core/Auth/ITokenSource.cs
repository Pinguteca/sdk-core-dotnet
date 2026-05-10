using System.Threading;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Core.Auth;

/// <summary>
/// Yields a bearer token. Implementations own their caching and
/// refresh behaviour; the auth interceptor calls
/// <see cref="GetTokenAsync(CancellationToken)"/> once per RPC.
///
/// Bring-your-own implementations cover any non-vendor IdP
/// (Keycloak, Auth0, Entra ID via OIDC, custom token services).
/// Nothing in this package is vendor-locked.
/// </summary>
public interface ITokenSource
{
    /// <summary>
    /// Returns a bearer token for the next outgoing call. The
    /// returned string is attached as the value half of
    /// <c>Authorization: Bearer &lt;token&gt;</c>.
    /// </summary>
    ValueTask<string> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Optional extension to <see cref="ITokenSource"/> for sources
/// whose cache can be explicitly invalidated. A future rotation
/// interceptor will call <see cref="Invalidate"/> when the server
/// rejects a token mid-call so the next
/// <see cref="ITokenSource.GetTokenAsync(CancellationToken)"/>
/// fetches a fresh token instead of serving the cached-and-
/// rejected one.
///
/// Implementations whose tokens cannot be rotated (e.g.
/// <see cref="StaticBearerTokenSource"/>) should not satisfy this
/// interface.
/// </summary>
public interface IRotatingTokenSource : ITokenSource
{
    /// <summary>Clears any cached token; thread-safe.</summary>
    void Invalidate();
}
