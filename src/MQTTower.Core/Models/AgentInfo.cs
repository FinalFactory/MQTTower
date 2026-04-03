namespace MQTTower.Core.Models;

/// <summary>Health/metadata returned by an agent <c>/health</c> endpoint.</summary>
public sealed class AgentInfo
{
    public string AgentVersion { get; set; } = string.Empty;
    public string? BrokerVersion { get; set; }
    public TimeSpan Uptime { get; set; }
    public bool MqttConnected { get; set; }
    public string? TlsCertThumbprint { get; set; }
}
