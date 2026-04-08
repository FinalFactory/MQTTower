namespace MQTTower.Core.Models;

public sealed class MqttClientInfo
{
    public string Username { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string> Roles { get; set; } = new();
    public List<string> Groups { get; set; } = new();
}
