using System;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Pinguteca.Sdk.Core.Mtls;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Tests;

public sealed class MtlsTests
{
    [Test]
    public async Task Config_DefaultOptions_TlsThirteen()
    {
        using var fx = new TestCerts();
        var ssl = MtlsHelper.Config(fx.CertPath, fx.KeyPath, caCertPath: null);
        await Assert.That(ssl.EnabledSslProtocols).IsEqualTo(SslProtocols.Tls13);
    }

    [Test]
    public async Task Config_AttachesClientCertificate()
    {
        using var fx = new TestCerts();
        var ssl = MtlsHelper.Config(fx.CertPath, fx.KeyPath, caCertPath: null);
        await Assert.That(ssl.ClientCertificates).IsNotNull();
        await Assert.That(ssl.ClientCertificates!.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Config_InsecureSkipVerify_Throws()
    {
        using var fx = new TestCerts();
        var ex = await Assert.That(() => MtlsHelper.Config(
            fx.CertPath,
            fx.KeyPath,
            caCertPath: null,
            new MtlsOptions { InsecureSkipVerify = true }))
            .ThrowsExactly<MtlsException>();
        await Assert.That(ex!.Code).IsEqualTo(MtlsErrorCode.InsecureSkipVerifyRejected);
    }

    [Test]
    public async Task Config_EmptyCertPath_Throws()
    {
        using var fx = new TestCerts();
        var ex = await Assert.That(() => MtlsHelper.Config("", fx.KeyPath, caCertPath: null))
            .ThrowsExactly<MtlsException>();
        await Assert.That(ex!.Code).IsEqualTo(MtlsErrorCode.EmptyCertPath);
    }

    [Test]
    public async Task Config_EmptyKeyPath_Throws()
    {
        using var fx = new TestCerts();
        var ex = await Assert.That(() => MtlsHelper.Config(fx.CertPath, "", caCertPath: null))
            .ThrowsExactly<MtlsException>();
        await Assert.That(ex!.Code).IsEqualTo(MtlsErrorCode.EmptyKeyPath);
    }

    [Test]
    public async Task Config_OverridesMinVersion()
    {
        using var fx = new TestCerts();
        var ssl = MtlsHelper.Config(
            fx.CertPath,
            fx.KeyPath,
            caCertPath: null,
            new MtlsOptions { MinVersion = SslProtocols.Tls12 });
        await Assert.That(ssl.EnabledSslProtocols).IsEqualTo(SslProtocols.Tls12);
    }

    [Test]
    public async Task Config_WithCaCert_AttachesValidationCallback()
    {
        using var fx = new TestCerts();
        var caPath = fx.WriteCaPem();
        var ssl = MtlsHelper.Config(fx.CertPath, fx.KeyPath, caPath);
        await Assert.That(ssl.RemoteCertificateValidationCallback).IsNotNull();
    }

    [Test]
    public async Task Config_NonPemCaFile_ThrowsInvalidPEM()
    {
        using var fx = new TestCerts();
        var binPath = Path.Combine(fx.Dir, "ca.bin");
        File.WriteAllBytes(binPath, new byte[] { 0x30, 0x82, 0x01, 0x00 });

        var ex = await Assert.That(() => MtlsHelper.Config(fx.CertPath, fx.KeyPath, binPath))
            .ThrowsExactly<MtlsException>();
        await Assert.That(ex!.Code).IsEqualTo(MtlsErrorCode.InvalidPEM);
    }

    [Test]
    public async Task Config_EmptyCaPemFile_ThrowsNoCAInFile()
    {
        using var fx = new TestCerts();
        var emptyCaPath = Path.Combine(fx.Dir, "empty-ca.pem");
        // PEM header but no actual certificate blocks
        File.WriteAllText(emptyCaPath, "-----BEGIN GIBBERISH-----\nzz\n-----END GIBBERISH-----\n");

        var ex = await Assert.That(() => MtlsHelper.Config(fx.CertPath, fx.KeyPath, emptyCaPath))
            .ThrowsExactly<MtlsException>();
        await Assert.That(ex!.Code).IsEqualTo(MtlsErrorCode.NoCAInFile);
    }

    [Test]
    public async Task ConfigFromP12_AttachesClientCertificate()
    {
        using var fx = new TestCerts();
        var ssl = MtlsHelper.ConfigFromP12(fx.P12Path, fx.P12Password, caCertPath: null);
        await Assert.That(ssl.ClientCertificates).IsNotNull();
        await Assert.That(ssl.ClientCertificates!.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ConfigFromP12_EmptyPath_Throws()
    {
        var ex = await Assert.That(() => MtlsHelper.ConfigFromP12("", "pw", caCertPath: null))
            .ThrowsExactly<MtlsException>();
        await Assert.That(ex!.Code).IsEqualTo(MtlsErrorCode.EmptyP12Path);
    }

    [Test]
    public async Task ConfigFromP12_InsecureSkipVerify_Throws()
    {
        using var fx = new TestCerts();
        var ex = await Assert.That(() => MtlsHelper.ConfigFromP12(
            fx.P12Path,
            fx.P12Password,
            caCertPath: null,
            new MtlsOptions { InsecureSkipVerify = true }))
            .ThrowsExactly<MtlsException>();
        await Assert.That(ex!.Code).IsEqualTo(MtlsErrorCode.InsecureSkipVerifyRejected);
    }

    [Test]
    public async Task ReadBoundedFile_OverSize_Throws()
    {
        using var fx = new TestCerts();
        var bigPath = Path.Combine(fx.Dir, "big.bin");
        // Slightly over the cap so the size check triggers.
        var bytes = new byte[MtlsHelper.MaxCertFileSize + 1];
        File.WriteAllBytes(bigPath, bytes);

        var ex = await Assert.That(() => MtlsHelper.ReadBoundedFile(bigPath, "test"))
            .ThrowsExactly<MtlsException>();
        await Assert.That(ex!.Code).IsEqualTo(MtlsErrorCode.CertFileTooLarge);
    }

    [Test]
    public async Task Transport_ReturnsHandlerWithSslOptions()
    {
        using var fx = new TestCerts();
        var ssl = MtlsHelper.Config(fx.CertPath, fx.KeyPath, caCertPath: null);
        using var handler = MtlsHelper.Transport(ssl);
        await Assert.That(handler.SslOptions).IsSameReferenceAs(ssl);
    }
}

internal sealed class TestCerts : IDisposable
{
    public string Dir { get; }
    public string CertPath { get; }
    public string KeyPath { get; }
    public string P12Path { get; }
    public string P12Password { get; } = "test-password";

    public TestCerts()
    {
        Dir = Path.Combine(Path.GetTempPath(), "mtls-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "cn=test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        CertPath = Path.Combine(Dir, "cert.pem");
        KeyPath = Path.Combine(Dir, "key.pem");
        P12Path = Path.Combine(Dir, "cert.p12");

        File.WriteAllText(CertPath, cert.ExportCertificatePem());
        File.WriteAllText(KeyPath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllBytes(P12Path, cert.Export(X509ContentType.Pkcs12, P12Password));
    }

    public string WriteCaPem()
    {
        // Reuse the same cert as a CA bundle for testing purposes; the
        // helper only checks the PEM shape and chain build, not whether
        // the cert is actually a CA.
        var ca = Path.Combine(Dir, "ca.pem");
        File.Copy(CertPath, ca, overwrite: true);
        return ca;
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch { }
    }
}
