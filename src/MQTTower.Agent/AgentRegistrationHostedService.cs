using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Mqtt;

namespace MQTTower.Agent;

public sealed class AgentRegistrationHostedService : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<AgentOptions> _agentOpts;
    private readonly IOptionsMonitor<AgentOptions> _agentMonitor;
    private readonly MqttConnectionService _mqtt;
    private readonly ILogger<AgentRegistrationHostedService> _logger;

    public AgentRegistrationHostedService(
        IHttpClientFactory httpFactory,
        IOptions<AgentOptions> agentOpts,
        IOptionsMonitor<AgentOptions> agentMonitor,
        MqttConnectionService mqtt,
        ILogger<AgentRegistrationHostedService> logger)
    {
        _httpFactory = httpFactory;
        _agentOpts = agentOpts;
        _agentMonitor = agentMonitor;
        _mqtt = mqtt;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = _agentOpts.Value;
        if (!o.AutoRegister || string.IsNullOrWhiteSpace(o.DashboardUrl) || string.IsNullOrWhiteSpace(o.RegistrationToken))
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

        var delay = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqtt.IsConnected)
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var client = _httpFactory.CreateClient(nameof(AgentRegistrationHostedService));
                var baseUrl = o.DashboardUrl!.TrimEnd('/');
                var url = $"{baseUrl}/api/agents/register";
                var publicUrl = string.IsNullOrWhiteSpace(_agentMonitor.CurrentValue.PublicAgentUrl)
                    ? baseUrl
                    : _agentMonitor.CurrentValue.PublicAgentUrl!.TrimEnd('/');

                string? thumb = null;
                if (!string.IsNullOrEmpty(_agentMonitor.CurrentValue.CertificatePath) && File.Exists(_agentMonitor.CurrentValue.CertificatePath))
                {
                    try
                    {
                        var pwd = _agentMonitor.CurrentValue.CertificatePassword ?? string.Empty;
                        thumb = AgentTlsCertificate.GetThumbprintSha256(_agentMonitor.CurrentValue.CertificatePath, pwd);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not read cert thumbprint");
                    }
                }

                var body = new AgentRegisterDto
                {
                    RegistrationToken = o.RegistrationToken,
                    AgentUrl = publicUrl,
                    ApiKey = o.ApiKey,
                    TlsCertThumbprint = thumb,
                    Name = Environment.MachineName,
                };

                var resp = await client.PostAsJsonAsync(url, body, stoppingToken).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Registered with dashboard at {Url}", baseUrl);
                    return;
                }

                _logger.LogWarning("Dashboard registration failed: {Status}", resp.StatusCode);
            }
            catch (Exception ex)
            {
                if (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    _logger.LogWarning(
                        "Dashboard registration: connection refused to {DashboardUrl}. Set Agent__DashboardUrl to the web base URL (same port as ASPNETCORE_URLS, e.g. http://127.0.0.1:2000 if Kestrel listens on 2000).",
                        o.DashboardUrl);
                }
                else
                {
                    _logger.LogWarning(ex, "Dashboard registration attempt failed");
                }
            }

            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            delay = delay < TimeSpan.FromMinutes(2) ? delay + TimeSpan.FromSeconds(5) : delay;
        }
    }

    private sealed class AgentRegisterDto
    {
        [JsonPropertyName("registrationToken")]
        public string RegistrationToken { get; set; } = string.Empty;

        [JsonPropertyName("agentUrl")]
        public string AgentUrl { get; set; } = string.Empty;

        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("tlsCertThumbprint")]
        public string? TlsCertThumbprint { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
