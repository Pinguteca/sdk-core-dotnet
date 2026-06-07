using System.Net;
using System.Net.Http;

namespace Pinguteca.Sdk.Core.Pool;

/// <summary>
/// Builds a <see cref="SocketsHttpHandler"/> wired with the
/// connection-pool knobs in <see cref="PoolOptions"/>. Consumers
/// pass the result to <c>GrpcChannelOptions.HttpHandler</c>:
///
/// <code>
/// var handler = PoolHelper.CreateHandler();
/// using var channel = GrpcChannel.ForAddress(
///     "https://api.example.com",
///     new GrpcChannelOptions { HttpHandler = handler });
/// </code>
///
/// For HTTP/3, set the version on the gRPC channel itself:
///
/// <code>
/// new GrpcChannelOptions
/// {
///     HttpHandler = PoolHelper.CreateHandler(),
///     HttpVersion = HttpVersion.Version30,
///     HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
/// }
/// </code>
///
/// SocketsHttpHandler supports QUIC out of the box on .NET 7+;
/// pool knobs apply uniformly across HTTP/1.1, HTTP/2, and HTTP/3.
/// </summary>
public static class PoolHelper
{
    /// <summary>
    /// Construct a <see cref="SocketsHttpHandler"/> with the
    /// connection-pool knobs in <paramref name="options"/> applied.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(PoolOptions? options = null)
    {
        PoolOptions effective = options ?? new PoolOptions();
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = effective.PooledConnectionLifetime,
            PooledConnectionIdleTimeout = effective.PooledConnectionIdleTimeout,
            MaxConnectionsPerServer = effective.MaxConnectionsPerServer,
            EnableMultipleHttp2Connections = effective.EnableMultipleHttp2Connections,
        };
    }
}
