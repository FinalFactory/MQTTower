using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Agents;

public sealed class AgentGatewayFactory : IBrokerGatewayFactory
{
    private readonly MqttTowerOptions _towerOptions;
    private readonly ILogger<AgentGatewayFactory> _logger;

    public AgentGatewayFactory(IOptions<MqttTowerOptions> towerOptions, ILogger<AgentGatewayFactory> logger)
    {
        _towerOptions = towerOptions.Value;
        _logger = logger;
    }

    public IBrokerGateway Create(BrokerProfile broker)
    {
        if (string.IsNullOrWhiteSpace(broker.AgentUrl))
        {
            throw new InvalidOperationException("Agent URL is required. Configure the co-located agent URL for the local broker.");
        }

        var baseUri = new Uri(broker.AgentUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
        };

        if (string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            TryAddClientCertificate(handler);
            handler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                ValidateServerCertificate(broker, certificate, errors, _towerOptions, _logger);
        }

        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        if (!string.IsNullOrEmpty(broker.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", broker.ApiKey);
        }

        return new AgentHttpClient(broker.Id, client);
    }

    private void TryAddClientCertificate(SocketsHttpHandler handler)
    {
        var path = _towerOptions.AgentClientCertPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var clientCert = X509CertificateLoader.LoadPkcs12FromFile(
                path,
                _towerOptions.AgentClientCertPassword ?? string.Empty,
                X509KeyStorageFlags.EphemeralKeySet);
            handler.SslOptions.ClientCertificates!.Add(clientCert);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load agent client certificate from {Path}", path);
        }
    }

    /// <summary>HTTPS remote certificate validation (thumbprint pin, optional custom CA, or TOFU-style acceptance of chain errors).</summary>
    internal static bool ValidateServerCertificate(BrokerProfile broker, X509Certificate? certificate, SslPolicyErrors errors, MqttTowerOptions? towerOptions = null, ILogger? logger = null)
    {
        if (certificate is null)
        {
            return false;
        }

        using var cert2 = new X509Certificate2(certificate);
        var sha256 = cert2.GetCertHashString(HashAlgorithmName.SHA256);
        if (!string.IsNullOrEmpty(broker.TlsCertThumbprint))
        {
            return string.Equals(sha256, broker.TlsCertThumbprint, StringComparison.OrdinalIgnoreCase);
        }

        var caPath = towerOptions?.AgentTlsServerCaCertPath;
        if (!string.IsNullOrWhiteSpace(caPath) && File.Exists(caPath))
        {
            try
            {
                using var ca = X509CertificateLoader.LoadCertificateFromFile(caPath);
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(ca);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                if (chain.Build(cert2))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Custom CA chain build failed for broker; falling back to thumbprint/TOFU rules");
            }
        }

        return errors == SslPolicyErrors.None || errors == SslPolicyErrors.RemoteCertificateChainErrors;
    }
}
