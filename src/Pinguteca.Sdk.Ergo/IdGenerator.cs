using System;
using System.Security.Cryptography;

namespace Pinguteca.Sdk.Ergo;

/// <summary>
/// Generates 128-bit hex identifiers via
/// <see cref="RandomNumberGenerator.Fill(Span{byte})"/>. The
/// underlying CSPRNG is FIPS-approved on every supported platform
/// (Windows BCryptGenRandom, Linux getrandom, macOS SecRandomCopyBytes
/// all delegate to SP 800-90A DRBGs).
/// </summary>
public static class IdGenerator
{
    /// <summary>
    /// Number of random bytes per generated id. 128 bits matches
    /// UUID v4 strength; collisions are effectively impossible at
    /// SDK call volumes.
    /// </summary>
    public const int IdByteLength = 16;

    /// <summary>
    /// Returns a 32-character hex string seeded from the platform
    /// CSPRNG. Throws <see cref="CryptographicException"/> only when
    /// the OS reports an entropy failure, which is effectively never
    /// on production hardware.
    /// </summary>
    public static string NewId()
    {
        Span<byte> buffer = stackalloc byte[IdByteLength];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexStringLower(buffer);
    }
}
