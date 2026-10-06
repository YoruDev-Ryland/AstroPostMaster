using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AstroPostMaster.Handoff;

/// <summary>
/// Self-signed TLS certificate for the phone page, persisted so phones only see the trust warning again when the
/// LAN address changes or the certificate nears expiry. Meets iOS's TLS rules: RSA-2048, SHA-256, serverAuth EKU,
/// SAN entries, validity at most 825 days.
/// </summary>
public static class HandoffCertificate
{
    public static readonly TimeSpan Validity = TimeSpan.FromDays(800);
    private static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);
    private const string SanOid = "2.5.29.17";

    public static X509Certificate2 LoadOrCreate(string pfxPath, IEnumerable<IPAddress> addresses, TimeProvider? time = null)
    {
        var now = (time ?? TimeProvider.System).GetUtcNow();
        var wanted = addresses.ToList();

        if (File.Exists(pfxPath))
        {
            try
            {
                var existing = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, null);
                if (existing.NotAfter.ToUniversalTime() - now.UtcDateTime > RenewBefore && Covers(existing, wanted)) return existing;
                existing.Dispose();
            }
            catch (CryptographicException)
            {
                // Unreadable file: fall through and replace it.
            }
        }

        var created = Create(wanted, now);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfxPath))!);
        File.WriteAllBytes(pfxPath, created.Export(X509ContentType.Pfx));
        return created;
    }

    internal static X509Certificate2 Create(IReadOnlyList<IPAddress> addresses, DateTimeOffset now)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=AstroPostMaster", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        foreach (var address in addresses) san.AddIpAddress(address);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

        using var cert = request.CreateSelfSigned(now.AddDays(-1), now + Validity);
        // Round-trip through PFX so the private key is usable by SslStream on every OS.
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    private static bool Covers(X509Certificate2 cert, IReadOnlyList<IPAddress> addresses)
    {
        var raw = cert.Extensions.FirstOrDefault(e => e.Oid?.Value == SanOid);
        if (raw is null) return addresses.Count == 0;
        var present = new X509SubjectAlternativeNameExtension(raw.RawData).EnumerateIPAddresses().ToHashSet();
        return addresses.All(present.Contains);
    }
}
