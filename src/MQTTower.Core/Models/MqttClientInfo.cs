namespace MQTTower.Core.Models;

public sealed class MqttClientInfo
{
    public string Username { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public bool Enabled { get; set; } = true;
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Groups { get; set; } = Array.Empty<string>();
}
