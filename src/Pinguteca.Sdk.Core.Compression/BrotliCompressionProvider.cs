using System.IO;
using System.IO.Compression;
using Grpc.Net.Compression;

namespace Pinguteca.Sdk.Core.Compression;

/// <summary>
/// gRPC compression provider that wraps the stdlib
/// <see cref="BrotliStream"/>. Registered as encoding name
/// <c>br</c>.
///
/// Cross-SDK contract from
/// <c>sdk-scaffold/docs/rfc/0011-compression-strategy.md</c>:
/// Brotli is the default Send encoding because of its ~95% proxy
/// and CDN acceptance and best-in-class ratio for text-heavy
/// payloads (JSON, text-encoded protobuf).
///
/// Compression level: <see cref="CompressionLevel.Optimal"/>, which
/// the .NET 5+ Brotli implementation maps to quality 4 (the level
/// pinned by RFC 0011 across SDKs: ~90% of best-level ratio at a
/// fraction of the CPU cost).
/// </summary>
public sealed class BrotliCompressionProvider : ICompressionProvider
{
    /// <inheritdoc />
    public string EncodingName => CompressionNames.Brotli;

    /// <inheritdoc />
    public Stream CreateCompressionStream(Stream stream, CompressionLevel? compressionLevel)
    {
        return new BrotliStream(stream, compressionLevel ?? CompressionLevel.Optimal);
    }

    /// <inheritdoc />
    public Stream CreateDecompressionStream(Stream stream)
    {
        return new BrotliStream(stream, CompressionMode.Decompress);
    }
}
