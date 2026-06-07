namespace Pinguteca.Sdk.Core.Pool;

/// <summary>
/// Knobs for the <see cref="System.Net.Http.SocketsHttpHandler"/>
/// that backs a gRPC channel's transport. The defaults match the
/// long-running-service profile most SDK consumers want: bounded
/// connection lifetime so DNS rotation does not strand traffic on
/// stale endpoints, idle timeout that lets the OS reclaim idle
/// sockets, and multiple HTTP/2 connections so a flood of long-
/// lived streams does not block on a single connection's
/// MAX_CONCURRENT_STREAMS.
/// </summary>
public sealed class PoolOptions
{
    /// <summary>
    /// How long a pooled connection lives before the handler closes
    /// it and forces a new one. RFC-style endpoints rarely rotate
    /// faster than this; lifetimes shorter than 1 minute trigger a
    /// reconnect storm under load. Default 5 minutes.
    /// </summary>
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long an idle pooled connection lingers before the handler
    /// drops it. Default 2 minutes; pair with the lifetime above so
    /// the handler stops re-using a connection well before a load
    /// balancer might silently terminate it.
    /// </summary>
    public TimeSpan PooledConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum concurrent connections to a single server. gRPC
    /// multiplexes streams over an HTTP/2 connection, so this is
    /// effectively a knob for how many HTTP/2 connections the pool
    /// can open in parallel (see also
    /// <see cref="EnableMultipleHttp2Connections"/>). Default
    /// <see cref="int.MaxValue"/>.
    /// </summary>
    public int MaxConnectionsPerServer { get; init; } = int.MaxValue;

    /// <summary>
    /// Allow the handler to open additional HTTP/2 connections when
    /// the first one's MAX_CONCURRENT_STREAMS is exhausted. Default
    /// true; required for high-fan-in workloads where a single gRPC
    /// channel multiplexes hundreds of concurrent streams.
    /// </summary>
    public bool EnableMultipleHttp2Connections { get; init; } = true;
}
