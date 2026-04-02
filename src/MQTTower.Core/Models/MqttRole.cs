namespace MQTTower.Core.Models;

public sealed class MqttRole
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<AclEntry> Acls { get; set; } = Array.Empty<AclEntry>();
}
