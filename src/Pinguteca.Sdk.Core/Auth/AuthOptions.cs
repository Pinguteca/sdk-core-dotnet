using System;

namespace Pinguteca.Sdk.Core.Auth;

/// <summary>
/// Configures <see cref="AuthInterceptor"/>. The interceptor
/// attaches a credential to every outgoing call by reading from
/// <see cref="Source"/> and writing the result to the metadata
/// entry named <see cref="HeaderName"/> (default
/// <c>"Authorization"</c>) formatted by
/// <see cref="FormatHeader"/> (default <c>"Bearer "</c> + token).
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Source of bearer tokens. Required.</summary>
    public required ITokenSource Source { get; init; }

    /// <summary>
    /// Metadata header name. Default <c>"Authorization"</c>. Lower-
    /// case at the wire per HTTP/2 conventions; gRPC's
    /// <see cref="Grpc.Core.Metadata"/> normalises header keys.
    /// </summary>
    public string HeaderName { get; init; } = "Authorization";

    /// <summary>
    /// Format function. Default produces <c>"Bearer &lt;token&gt;"</c>.
    /// Override for API-key headers, custom schemes, or vendor
    /// quirks.
    /// </summary>
    public Func<string, string> FormatHeader { get; init; } = static token => "Bearer " + token;

    /// <summary>
    /// Optional predicate that suppresses credential attachment for
    /// specific methods. Returns true when the method is unauthenticated
    /// (e.g. a health-check RPC). Method names use the canonical
    /// <c>/svc.Service/Method</c> shape that gRPC exposes.
    /// </summary>
    public Func<string, bool>? Skip { get; init; }
}
