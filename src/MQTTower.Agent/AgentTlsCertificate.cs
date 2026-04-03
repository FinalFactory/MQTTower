using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MQTTower.Agent;

internal static class AgentTlsCertificate
{
    public static void EnsureSelfSignedPfx(string pfxPath, string password)
    {
        if (File.Exists(pfxPath))
        {
            return;
        }

        var dir = Path.GetDirectoryName(pfxPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=mqttower-agent", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var pfx = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfx);
    }

    public static string GetThumbprintSha256(string pfxPath, string password)
    {
        using var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password);
        return cert.GetCertHashString(HashAlgorithmName.SHA256);
    }
}
