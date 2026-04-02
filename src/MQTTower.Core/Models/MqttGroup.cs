namespace MQTTower.Core.Models;

public sealed class MqttGroup
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<string> RoleNames { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ClientUsernames { get; set; } = Array.Empty<string>();
}
