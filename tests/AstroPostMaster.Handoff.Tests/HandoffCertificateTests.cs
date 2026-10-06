using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Time.Testing;

namespace AstroPostMaster.Handoff.Tests;

public class HandoffCertificateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "apm-cert-" + Guid.NewGuid().ToString("N"));
    private string Pfx => Path.Combine(_dir, "handoff-cert.pfx");
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.50");

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void LoadOrCreate_CreatesAnIosCompatibleServerCertificate()
    {
        using var cert = HandoffCertificate.LoadOrCreate(Pfx, [Lan]);

        Assert.True(File.Exists(Pfx));
        Assert.True(cert.HasPrivateKey);
        Assert.Equal(2048, cert.GetRSAPublicKey()!.KeySize);
        Assert.Equal("sha256RSA", cert.SignatureAlgorithm.FriendlyName);
        Assert.True((cert.NotAfter - cert.NotBefore).TotalDays <= 825);
        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), o => o.Value == "1.3.6.1.5.5.7.3.1");
        var san = new X509SubjectAlternativeNameExtension(cert.Extensions.Single(e => e.Oid!.Value == "2.5.29.17").RawData);
        Assert.Contains(Lan, san.EnumerateIPAddresses());
    }

    [Fact]
    public void LoadOrCreate_ReusesTheSavedCertificate()
    {
        using var first = HandoffCertificate.LoadOrCreate(Pfx, [Lan]);
        using var second = HandoffCertificate.LoadOrCreate(Pfx, [Lan]);
        Assert.Equal(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void LoadOrCreate_RegeneratesWhenTheLanAddressChanges()
    {
        using var first = HandoffCertificate.LoadOrCreate(Pfx, [Lan]);
        using var second = HandoffCertificate.LoadOrCreate(Pfx, [IPAddress.Parse("10.0.0.7")]);
        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void LoadOrCreate_RegeneratesNearExpiry()
    {
        using var first = HandoffCertificate.LoadOrCreate(Pfx, [Lan]);
        var later = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(790));
        using var second = HandoffCertificate.LoadOrCreate(Pfx, [Lan], later);
        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void LoadOrCreate_ReplacesACorruptFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Pfx, "garbage");
        using var cert = HandoffCertificate.LoadOrCreate(Pfx, [Lan]);
        Assert.True(cert.HasPrivateKey);
    }
}
