using System.IO;
using System.IO.Compression;
using Grpc.Net.Compression;
using ZstdSharp;

namespace Pinguteca.Sdk.Core.Compression;

/// <summary>
/// gRPC compression provider that wraps the pure-managed
/// <c>ZstdSharp.Port</c> implementation of Zstandard. Registered as
/// encoding name <c>zstd</c>.
///
/// Cross-SDK contract from
/// <c>sdk-scaffold/docs/rfc/0011-compression-strategy.md</c>:
/// Zstd is opt-in via Accept-Encoding negotiation because
/// server-side acceptance is only ~70% as of 2026; consumers who
/// control both ends of a high-throughput deployment can flip the
/// default Send encoding to <c>zstd</c>.
///
/// Compression level: library default. Per-call tuning is deferred
/// until production traffic reports a wrong trade-off.
///
/// This provider ships in the Layer 3 companion package because the
/// pure-managed Zstd implementation is a third-party dependency
/// (<c>ZstdSharp.Port</c>). Once <c>System.IO.Compression.ZStandardStream</c>
/// reaches the stdlib (rumoured for .NET 11), the provider migrates
/// to stdlib and the package may collapse into core.
/// </summary>
public sealed class ZstdCompressionProvider : ICompressionProvider
{
    /// <inheritdoc />
    public string EncodingName => CompressionNames.Zstd;

    /// <inheritdoc />
    public Stream CreateCompressionStream(Stream stream, CompressionLevel? compressionLevel)
    {
        return new CompressionStream(stream);
    }

    /// <inheritdoc />
    public Stream CreateDecompressionStream(Stream stream)
    {
        return new DecompressionStream(stream);
    }
}
