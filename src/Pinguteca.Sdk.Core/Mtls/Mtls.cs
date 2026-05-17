using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Pinguteca.Sdk.Core.Mtls;

/// <summary>
/// Builds <see cref="SslClientAuthenticationOptions"/> and a
/// configured <see cref="SocketsHttpHandler"/> for client mTLS in
/// standalone deployments. Transport-layer helper, not an
/// interceptor; TLS negotiation belongs below the gRPC interceptor
/// chain per
/// <c>sdk-scaffold/docs/rfc/0014-mtls-helper.md</c>.
///
/// PEM and PKCS#12 both ship in this Layer 2 module because every
/// .NET runtime ships stdlib parsers for both
/// (<see cref="X509Certificate2.CreateFromPemFile"/> and
/// <see cref="X509Certificate2(byte[], string?)"/> respectively).
/// Languages whose PKCS#12 parser is a third-party dependency (Go,
/// Rust) split PKCS#12 into a Layer 3 sub-module.
///
/// Mesh-resident services should not wire this helper: the sidecar
/// or eBPF redirector already terminates mTLS, and wiring SDK mTLS
/// breaks the handshake. Mesh awareness lives in the preset
/// documentation, not in the helper.
/// </summary>
public static class MtlsHelper
{
    /// <summary>
    /// Maximum bytes read from any certificate or key file. Real
    /// bundles are well under 100 KiB; the 1 MiB ceiling bounds DoS
    /// pressure if a caller points the helper at an unexpected file
    /// (a bind mount that turned into a log, a sparse file).
    /// </summary>
    public const long MaxCertFileSize = 1 << 20;

    private static readonly byte[] PemHeaderPrefix = Encoding.ASCII.GetBytes("-----BEGIN ");

    /// <summary>
    /// Loads a PEM-encoded client certificate and key from disk and
    /// returns a <see cref="SslClientAuthenticationOptions"/> with
    /// the cross-SDK defaults applied. <paramref name="caCertPath"/>
    /// may be null or empty, in which case only the system root pool
    /// is used for server validation.
    /// </summary>
    public static SslClientAuthenticationOptions Config(
        string certPath,
        string keyPath,
        string? caCertPath,
        MtlsOptions? options = null)
    {
        var opts = options ?? new MtlsOptions();
        GuardOptions(opts);
        if (string.IsNullOrEmpty(certPath))
        {
            throw new MtlsException(MtlsErrorCode.EmptyCertPath, "mtls: cert path is empty");
        }
        if (string.IsNullOrEmpty(keyPath))
        {
            throw new MtlsException(MtlsErrorCode.EmptyKeyPath, "mtls: key path is empty");
        }

        X509Certificate2 cert;
        try
        {
            var cleanedCert = Path.GetFullPath(certPath);
            var cleanedKey = Path.GetFullPath(keyPath);
            // Bound the reads first so a bad path cannot exhaust memory
            // before the X509 layer fails.
            _ = ReadBoundedFile(cleanedCert, "cert");
            _ = ReadBoundedFile(cleanedKey, "key");
            cert = X509Certificate2.CreateFromPemFile(cleanedCert, cleanedKey);
        }
        catch (MtlsException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MtlsException(
                MtlsErrorCode.LoadFailed,
                "mtls: load PEM cert/key: " + ex.Message,
                ex);
        }

        return Assemble(cert, caCertPath, opts);
    }

    /// <summary>
    /// Loads a PKCS#12 / PFX bundle from disk and returns a
    /// <see cref="SslClientAuthenticationOptions"/> with the same
    /// defaults as <see cref="Config"/>. <paramref name="password"/>
    /// may be null or empty for unencrypted PFX bundles.
    /// </summary>
    public static SslClientAuthenticationOptions ConfigFromP12(
        string p12Path,
        string? password,
        string? caCertPath,
        MtlsOptions? options = null)
    {
        var opts = options ?? new MtlsOptions();
        GuardOptions(opts);
        if (string.IsNullOrEmpty(p12Path))
        {
            throw new MtlsException(MtlsErrorCode.EmptyP12Path, "mtls: p12 path is empty");
        }

        X509Certificate2 cert;
        try
        {
            var cleaned = Path.GetFullPath(p12Path);
            var bytes = ReadBoundedFile(cleaned, "p12");
            cert = X509CertificateLoader.LoadPkcs12(
                bytes,
                password,
                X509KeyStorageFlags.DefaultKeySet);
        }
        catch (MtlsException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MtlsException(
                MtlsErrorCode.LoadFailed,
                "mtls: load PKCS#12: " + ex.Message,
                ex);
        }

        return Assemble(cert, caCertPath, opts);
    }

    /// <summary>
    /// Returns a fresh <see cref="SocketsHttpHandler"/> configured
    /// with the supplied <paramref name="sslOptions"/>. The handler
    /// is suitable for <c>GrpcChannel.ForAddress</c> via
    /// <c>GrpcChannelOptions.HttpHandler</c>.
    /// </summary>
    public static SocketsHttpHandler Transport(SslClientAuthenticationOptions sslOptions)
    {
        ArgumentNullException.ThrowIfNull(sslOptions);
        return new SocketsHttpHandler
        {
            SslOptions = sslOptions,
        };
    }

    /// <summary>
    /// Turns an already-loaded <see cref="X509Certificate2"/> into a
    /// <see cref="SslClientAuthenticationOptions"/> using the
    /// package's standard CA-loading and option-validation rules.
    /// Sub-modules that handle additional cert formats reuse this so
    /// the resulting options share the same TLS 1.3 default and
    /// <see cref="MtlsOptions.InsecureSkipVerify"/> rejection.
    /// </summary>
    public static SslClientAuthenticationOptions Assemble(
        X509Certificate2 cert,
        string? caCertPath,
        MtlsOptions options)
    {
        ArgumentNullException.ThrowIfNull(cert);
        ArgumentNullException.ThrowIfNull(options);
        GuardOptions(options);

        var customCas = LoadCustomCas(caCertPath);
        var sslOptions = new SslClientAuthenticationOptions
        {
            ClientCertificates = new X509CertificateCollection { cert },
            EnabledSslProtocols = options.MinVersion,
        };
        if (customCas is not null)
        {
            sslOptions.RemoteCertificateValidationCallback =
                BuildAppendingValidationCallback(customCas);
        }
        return sslOptions;
    }

    /// <summary>
    /// Cleans the path, opens the file, and reads up to
    /// <see cref="MaxCertFileSize"/> bytes. Anything larger is
    /// rejected before allocation. Exposed so PKCS#12 sub-modules
    /// (in languages where PKCS#12 is third-party) can reuse the
    /// same hardening.
    /// </summary>
    public static byte[] ReadBoundedFile(string path, string kind)
    {
        var cleaned = Path.GetFullPath(path);
        using var stream = File.OpenRead(cleaned);
        if (stream.Length > MaxCertFileSize)
        {
            throw new MtlsException(
                MtlsErrorCode.CertFileTooLarge,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "mtls: {0} file exceeds MaxCertFileSize ({1} bytes): {2}",
                    kind,
                    MaxCertFileSize,
                    cleaned));
        }
        var buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static void GuardOptions(MtlsOptions options)
    {
        if (options.InsecureSkipVerify)
        {
            throw new MtlsException(
                MtlsErrorCode.InsecureSkipVerifyRejected,
                "mtls: InsecureSkipVerify is not allowed");
        }
    }

    private static X509Certificate2Collection? LoadCustomCas(string? caCertPath)
    {
        if (string.IsNullOrEmpty(caCertPath))
        {
            return null;
        }
        var raw = ReadBoundedFile(caCertPath, "CA");
        EnsurePem(raw, caCertPath);

        var collection = new X509Certificate2Collection();
        collection.ImportFromPem(Encoding.UTF8.GetString(raw));
        if (collection.Count == 0)
        {
            throw new MtlsException(
                MtlsErrorCode.NoCAInFile,
                "mtls: no PEM certificates in CA file: " + caCertPath);
        }
        return collection;
    }

    private static void EnsurePem(byte[] raw, string path)
    {
        var span = raw.AsSpan();
        int start = 0;
        while (start < span.Length && IsAsciiWhitespace(span[start]))
        {
            start++;
        }
        if (span.Length - start < PemHeaderPrefix.Length ||
            !span.Slice(start, PemHeaderPrefix.Length).SequenceEqual(PemHeaderPrefix))
        {
            throw new MtlsException(
                MtlsErrorCode.InvalidPEM,
                "mtls: file is not a PEM-encoded certificate: " + path);
        }
    }

    private static bool IsAsciiWhitespace(byte b) => b is 0x20 or 0x09 or 0x0A or 0x0D;

    private static RemoteCertificateValidationCallback BuildAppendingValidationCallback(
        X509Certificate2Collection customCas)
    {
        return (sender, certificate, chain, errors) =>
        {
            if (errors == SslPolicyErrors.None)
            {
                // System pool already accepted it; no need to inspect
                // custom CAs.
                return true;
            }
            if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                // Name mismatch or no cert supplied; do not bypass.
                return false;
            }
            if (certificate is not X509Certificate2 c2)
            {
                return false;
            }
            using var customChain = new X509Chain();
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            customChain.ChainPolicy.CustomTrustStore.AddRange(customCas);
            return customChain.Build(c2);
        };
    }
}
