namespace MQTTower.Core.Models;

/// <summary>Registered MQTT broker managed via an agent HTTP API.</summary>
public sealed class BrokerProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Base URL of the agent (e.g. https://agent:5100 or http://127.0.0.1:5080).</summary>
    public string AgentUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? TlsCertThumbprint { get; set; }
    public BrokerStatus Status { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public bool Approved { get; set; }
    public string? Notes { get; set; }
    /// <summary>Built-in local broker profile (e.g. install/uninstall from dashboard); not deletable like remote registrations.</summary>
    public bool UseLocalServices { get; set; }
}
