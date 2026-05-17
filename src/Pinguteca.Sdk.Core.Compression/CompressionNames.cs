namespace Pinguteca.Sdk.Core.Compression;

/// <summary>
/// Canonical encoding names used in the gRPC <c>grpc-encoding</c> and
/// <c>grpc-accept-encoding</c> headers. Aligned with the values pinned
/// by RFC 0011 across every SDK.
/// </summary>
public static class CompressionNames
{
    /// <summary>Brotli encoding (<c>br</c>). Default Send encoding.</summary>
    public const string Brotli = "br";

    /// <summary>Zstandard encoding (<c>zstd</c>).</summary>
    public const string Zstd = "zstd";

    /// <summary>Gzip encoding (<c>gzip</c>). Registered by Grpc.Net.Client by default.</summary>
    public const string Gzip = "gzip";
}
