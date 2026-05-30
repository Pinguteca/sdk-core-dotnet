using System;
using System.Security.Cryptography;
using Google.Protobuf;

namespace Pinguteca.Sdk.Core.Caching;

/// <summary>
/// Composes the cache key per RFC 0015 as
/// <c>{scope}:{method}:{sha256(serialised-request)}</c>. SHA-256 is
/// FIPS 180-4 approved; the hash keeps the key size stable
/// regardless of payload and avoids leaking request bodies into
/// shared cache logs.
/// </summary>
public static class CacheKey
{
    /// <summary>
    /// Builds a cache key for the supplied tenant scope, fully
    /// qualified procedure path, and serialised request bytes.
    /// </summary>
    public static string Build(string scope, string method, ReadOnlySpan<byte> requestBytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(requestBytes, hash);
        return string.Concat(scope, ":", method, ":", Convert.ToHexStringLower(hash));
    }

    /// <summary>
    /// Serialises an <see cref="IMessage"/> request to bytes. Used
    /// in tandem with <see cref="Build"/>.
    /// </summary>
    public static byte[] SerializeRequest(IMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.ToByteArray();
    }
}
