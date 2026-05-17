using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pinguteca.Sdk.Core.Compression;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Compression.Tests;

public sealed class CompressionProviderTests
{
    private static readonly byte[] _payload = Encoding.UTF8.GetBytes(
        "the quick brown fox jumps over the lazy dog. the quick brown fox jumps over the lazy dog. " +
        "the quick brown fox jumps over the lazy dog. the quick brown fox jumps over the lazy dog. " +
        "the quick brown fox jumps over the lazy dog. the quick brown fox jumps over the lazy dog.");

    [Test]
    public async Task Brotli_EncodingNameMatchesRfc()
    {
        var provider = new BrotliCompressionProvider();
        await Assert.That(provider.EncodingName).IsEqualTo("br");
    }

    [Test]
    public async Task Zstd_EncodingNameMatchesRfc()
    {
        var provider = new ZstdCompressionProvider();
        await Assert.That(provider.EncodingName).IsEqualTo("zstd");
    }

    [Test]
    public async Task Brotli_RoundTripsPayload()
    {
        var provider = new BrotliCompressionProvider();
        var roundTripped = await RoundTripAsync(provider, _payload);
        await Assert.That(roundTripped).IsEquivalentTo(_payload);
    }

    [Test]
    public async Task Brotli_CompressionShrinksRepetitivePayload()
    {
        var provider = new BrotliCompressionProvider();
        var compressed = await CompressAsync(provider, _payload);
        await Assert.That(compressed.Length).IsLessThan(_payload.Length);
    }

    [Test]
    public async Task Zstd_RoundTripsPayload()
    {
        var provider = new ZstdCompressionProvider();
        var roundTripped = await RoundTripAsync(provider, _payload);
        await Assert.That(roundTripped).IsEquivalentTo(_payload);
    }

    [Test]
    public async Task Zstd_CompressionShrinksRepetitivePayload()
    {
        var provider = new ZstdCompressionProvider();
        var compressed = await CompressAsync(provider, _payload);
        await Assert.That(compressed.Length).IsLessThan(_payload.Length);
    }

    [Test]
    public async Task Defaults_ProvidersListBrotliFirstThenZstd()
    {
        var providers = CompressionDefaults.CreateProviders();

        await Assert.That(providers.Count).IsEqualTo(2);
        await Assert.That(providers[0]).IsTypeOf<BrotliCompressionProvider>();
        await Assert.That(providers[1]).IsTypeOf<ZstdCompressionProvider>();
    }

    [Test]
    public async Task Defaults_AddToPreservesExistingProviders()
    {
        var options = new Grpc.Net.Client.GrpcChannelOptions
        {
            CompressionProviders = new[] { new BrotliCompressionProvider() },
        };

        CompressionDefaults.AddTo(options);

        await Assert.That(options.CompressionProviders!.Count).IsEqualTo(3);
        await Assert.That(options.CompressionProviders.Any(p => p.EncodingName == "zstd")).IsTrue();
    }

    [Test]
    public async Task Defaults_DefaultSendEncodingIsBrotli()
    {
        await Assert.That(CompressionDefaults.DefaultSendEncoding).IsEqualTo("br");
    }

    private static async Task<byte[]> CompressAsync(
        Grpc.Net.Compression.ICompressionProvider provider,
        byte[] payload)
    {
        using var destination = new MemoryStream();
        await using (var compressor = provider.CreateCompressionStream(destination, CompressionLevel.Optimal))
        {
            await compressor.WriteAsync(payload).ConfigureAwait(false);
        }
        return destination.ToArray();
    }

    private static async Task<byte[]> RoundTripAsync(
        Grpc.Net.Compression.ICompressionProvider provider,
        byte[] payload)
    {
        var compressed = await CompressAsync(provider, payload);

        using var source = new MemoryStream(compressed);
        await using var decompressor = provider.CreateDecompressionStream(source);
        using var output = new MemoryStream();
        await decompressor.CopyToAsync(output).ConfigureAwait(false);
        return output.ToArray();
    }
}
