namespace MQTTower.Core.Models;

/// <summary>Registered MQTT broker managed via an agent or in-process services.</summary>
public sealed class BrokerProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Base URL of the agent (e.g. https://agent:5100). Ignored when <see cref="UseLocalServices"/> is true.</summary>
    public string AgentUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? TlsCertThumbprint { get; set; }
    public BrokerStatus Status { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public bool Approved { get; set; }
    public string? Notes { get; set; }
    /// <summary>Use in-process MQTT/DynSec (dashboard co-located with broker stack).</summary>
    public bool UseLocalServices { get; set; }
}
