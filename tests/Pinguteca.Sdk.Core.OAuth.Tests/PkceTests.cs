using System;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Assertions.Extensions.Throws;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.OAuth.Tests;

public sealed class PkceTests
{
    [Test]
    public async Task Generate_DefaultVerifierIs43Chars()
    {
        var pair = PkcePair.Generate();

        // 32 random bytes encoded as base64url-no-pad = 43 ASCII chars.
        await Assert.That(pair.Verifier.Length).IsEqualTo(43);
    }

    [Test]
    public async Task Generate_ChallengeMethodIsAlwaysS256()
    {
        await Assert.That(PkcePair.ChallengeMethod).IsEqualTo("S256");
    }

    [Test]
    public async Task Generate_TwoCallsProduceDistinctVerifiers()
    {
        // Smoke test for entropy. Two consecutive 256-bit draws colliding
        // would mean the RNG is broken.
        var a = PkcePair.Generate();
        var b = PkcePair.Generate();

        await Assert.That(a.Verifier).IsNotEqualTo(b.Verifier);
        await Assert.That(a.Challenge).IsNotEqualTo(b.Challenge);
    }

    [Test]
    [Arguments(32)]
    [Arguments(48)]
    [Arguments(96)]
    public async Task Generate_AcceptsAllowedByteLengths(int bytes)
    {
        var pair = PkcePair.Generate(bytes);

        await Assert.That(pair.Verifier.Length).IsGreaterThanOrEqualTo(PkcePair.MinimumVerifierCharLength);
        await Assert.That(pair.Verifier.Length).IsLessThanOrEqualTo(PkcePair.MaximumVerifierCharLength);
    }

    [Test]
    [Arguments(0)]
    [Arguments(31)]
    [Arguments(97)]
    [Arguments(1024)]
    public async Task Generate_RejectsOutOfRangeByteLengths(int bytes)
    {
        await Assert.That(() => PkcePair.Generate(bytes))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task FromVerifier_DerivesExpectedChallengeForRfc7636AppendixBExample()
    {
        // RFC 7636 Appendix B test vector.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var pair = PkcePair.FromVerifier(verifier);

        await Assert.That(pair.Verifier).IsEqualTo(verifier);
        await Assert.That(pair.Challenge).IsEqualTo(expectedChallenge);
    }

    [Test]
    public async Task FromVerifier_RejectsTooShort()
    {
        string verifier = new string('a', PkcePair.MinimumVerifierCharLength - 1);

        await Assert.That(() => PkcePair.FromVerifier(verifier))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task FromVerifier_RejectsTooLong()
    {
        string verifier = new string('a', PkcePair.MaximumVerifierCharLength + 1);

        await Assert.That(() => PkcePair.FromVerifier(verifier))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task FromVerifier_RejectsReservedChars()
    {
        // '!' is not in the RFC 7636 unreserved set.
        string verifier = "!" + new string('a', PkcePair.MinimumVerifierCharLength - 1);

        await Assert.That(() => PkcePair.FromVerifier(verifier))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task FromVerifier_RejectsNull()
    {
        await Assert.That(() => PkcePair.FromVerifier(null!))
            .ThrowsExactly<ArgumentNullException>();
    }
}
