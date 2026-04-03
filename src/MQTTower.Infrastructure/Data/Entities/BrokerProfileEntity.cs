namespace MQTTower.Infrastructure.Data.Entities;

public sealed class BrokerProfileEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? TlsCertThumbprint { get; set; }
    public int Status { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public bool Approved { get; set; }
    public string? Notes { get; set; }
    public bool UseLocalServices { get; set; }
}
