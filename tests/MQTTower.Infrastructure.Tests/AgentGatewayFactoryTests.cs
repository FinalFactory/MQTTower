using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Agents;

namespace MQTTower.Infrastructure.Tests;

public sealed class AgentGatewayFactoryTests
{
    private static X509Certificate2 CreateEphemeralSelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("cn=mqttower-agent-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Fact]
    public void ValidateServerCertificate_null_certificate_rejected()
    {
        var broker = new BrokerProfile();
        AgentGatewayFactory.ValidateServerCertificate(broker, null, SslPolicyErrors.None).Should().BeFalse();
    }

    [Fact]
    public void ValidateServerCertificate_thumbprint_must_match_when_set()
    {
        using var cert = CreateEphemeralSelfSigned();
        var thumb = cert.GetCertHashString(HashAlgorithmName.SHA256);
        var broker = new BrokerProfile { TlsCertThumbprint = thumb };

        AgentGatewayFactory.ValidateServerCertificate(broker, cert, SslPolicyErrors.RemoteCertificateChainErrors)
            .Should().BeTrue();

        var brokerWrong = new BrokerProfile { TlsCertThumbprint = new string('f', thumb.Length) };
        AgentGatewayFactory.ValidateServerCertificate(brokerWrong, cert, SslPolicyErrors.None)
            .Should().BeFalse();
    }

    [Fact]
    public void ValidateServerCertificate_without_thumbprint_accepts_none_or_chain_errors_only()
    {
        using var cert = CreateEphemeralSelfSigned();
        var broker = new BrokerProfile { TlsCertThumbprint = null };

        AgentGatewayFactory.ValidateServerCertificate(broker, cert, SslPolicyErrors.None).Should().BeTrue();
        AgentGatewayFactory.ValidateServerCertificate(broker, cert, SslPolicyErrors.RemoteCertificateChainErrors)
            .Should().BeTrue();

        AgentGatewayFactory.ValidateServerCertificate(broker, cert, SslPolicyErrors.RemoteCertificateNameMismatch)
            .Should().BeFalse();
    }
}
