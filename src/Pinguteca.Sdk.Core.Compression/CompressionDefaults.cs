using System.Collections.Generic;
using Grpc.Net.Client;
using Grpc.Net.Compression;

namespace Pinguteca.Sdk.Core.Compression;

/// <summary>
/// Convenience wiring for the cross-SDK compression contract pinned
/// in <c>sdk-scaffold/docs/rfc/0011-compression-strategy.md</c>.
/// Registers Brotli and Zstd providers alongside Grpc.Net.Client's
/// built-in Gzip and exposes the encoding name used for the default
/// Send compression.
/// </summary>
public static class CompressionDefaults
{
    /// <summary>
    /// Encoding selected as the default outbound compression.
    /// Matches the value pinned for every SDK in RFC 0011.
    /// </summary>
    public const string DefaultSendEncoding = CompressionNames.Brotli;

    /// <summary>
    /// Returns the providers added on top of Grpc.Net.Client's
    /// default registrations. Gzip is already registered by the
    /// runtime; the caller adds these to
    /// <see cref="GrpcChannelOptions.CompressionProviders"/>.
    /// </summary>
    public static IReadOnlyList<ICompressionProvider> CreateProviders()
    {
        return new ICompressionProvider[]
        {
            new BrotliCompressionProvider(),
            new ZstdCompressionProvider(),
        };
    }

    /// <summary>
    /// Adds Brotli and Zstd providers to the supplied channel options
    /// in-place. Preserves any providers the caller has already
    /// configured.
    /// </summary>
    public static void AddTo(GrpcChannelOptions options)
    {
        var existing = options.CompressionProviders is null
            ? new List<ICompressionProvider>()
            : new List<ICompressionProvider>(options.CompressionProviders);

        existing.Add(new BrotliCompressionProvider());
        existing.Add(new ZstdCompressionProvider());

        options.CompressionProviders = existing;
    }
}
