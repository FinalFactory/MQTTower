namespace MQTTower.Core.Models;

public sealed class MqttGroup
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> RoleNames { get; set; } = new();
    public List<string> ClientUsernames { get; set; } = new();
}
