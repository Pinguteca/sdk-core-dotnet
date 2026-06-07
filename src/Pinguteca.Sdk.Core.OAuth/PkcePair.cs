using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Proof-Key-for-Code-Exchange (RFC 7636) verifier and challenge
/// pair used during the authorization-code grant.
/// </summary>
/// <remarks>
/// Per cross-SDK RFC 0017, the challenge method is always <c>S256</c>;
/// the <c>plain</c> method is not exposed. The verifier is sampled
/// from <see cref="RandomNumberGenerator"/> and the challenge is
/// derived as <c>base64url-no-pad(SHA-256(verifier))</c>. Both
/// primitives are FIPS 140-3 approved.
/// </remarks>
public sealed class PkcePair
{
    /// <summary>Fixed S256 challenge method per RFC 0017.</summary>
    public const string ChallengeMethod = "S256";

    /// <summary>Default verifier length when callers do not specify.</summary>
    /// <remarks>
    /// 32 random bytes encoded as base64url-no-pad yields 43 ASCII
    /// characters, the lower bound permitted by RFC 7636 section 4.1
    /// while delivering 256 bits of entropy.
    /// </remarks>
    public const int DefaultVerifierByteLength = 32;

    /// <summary>Minimum verifier byte length accepted by <see cref="FromVerifier"/>.</summary>
    /// <remarks>
    /// RFC 7636 mandates a verifier of 43 to 128 ASCII characters.
    /// Decoded base64url-no-pad bounds translate to roughly 32 to 96
    /// bytes; the lower bound is enforced literally on character
    /// length below.
    /// </remarks>
    public const int MinimumVerifierCharLength = 43;

    /// <summary>Maximum verifier character length permitted by RFC 7636.</summary>
    public const int MaximumVerifierCharLength = 128;

    private PkcePair(string verifier, string challenge)
    {
        Verifier = verifier;
        Challenge = challenge;
    }

    /// <summary>The base64url-no-pad verifier kept by the client.</summary>
    public string Verifier { get; }

    /// <summary>The base64url-no-pad SHA-256 of the verifier; sent to the IdP.</summary>
    public string Challenge { get; }

    /// <summary>
    /// Generate a fresh pair from <see cref="RandomNumberGenerator"/>.
    /// </summary>
    /// <param name="verifierByteLength">
    /// Number of random bytes to draw before base64url encoding. Must
    /// be between 32 and 96 inclusive; defaults to
    /// <see cref="DefaultVerifierByteLength"/>.
    /// </param>
    public static PkcePair Generate(int verifierByteLength = DefaultVerifierByteLength)
    {
        if (verifierByteLength is < 32 or > 96)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verifierByteLength),
                verifierByteLength,
                "Verifier byte length must be between 32 and 96 inclusive.");
        }

        Span<byte> bytes = stackalloc byte[96];
        bytes = bytes[..verifierByteLength];
        RandomNumberGenerator.Fill(bytes);

        string verifier = Base64Url.EncodeToString(bytes);
        string challenge = ComputeChallenge(verifier);
        return new PkcePair(verifier, challenge);
    }

    /// <summary>
    /// Rehydrate a pair from an existing verifier produced earlier
    /// (e.g. persisted across an interactive redirect).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="verifier"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Verifier is shorter than 43 characters, longer than 128, or
    /// contains characters outside the RFC 7636 unreserved set.
    /// </exception>
    public static PkcePair FromVerifier(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        if (verifier.Length is < MinimumVerifierCharLength or > MaximumVerifierCharLength)
        {
            throw new ArgumentException(
                $"Verifier must be {MinimumVerifierCharLength}-{MaximumVerifierCharLength} characters; got {verifier.Length}.",
                nameof(verifier));
        }

        // RFC 7636 section 4.1: verifier alphabet is unreserved URI
        // characters [A-Z] / [a-z] / [0-9] / "-" / "." / "_" / "~".
        foreach (char c in verifier)
        {
            if (!IsUnreservedUriChar(c))
            {
                throw new ArgumentException(
                    "Verifier contains a character outside the RFC 7636 unreserved set.",
                    nameof(verifier));
            }
        }

        return new PkcePair(verifier, ComputeChallenge(verifier));
    }

    private static string ComputeChallenge(string verifier)
    {
        Span<byte> verifierBytes = stackalloc byte[256];
        int written = Encoding.ASCII.GetBytes(verifier, verifierBytes);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(verifierBytes[..written], hash);
        return Base64Url.EncodeToString(hash);
    }

    private static bool IsUnreservedUriChar(char c)
        => c is (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z')
            or (>= '0' and <= '9')
            or '-' or '.' or '_' or '~';
}
