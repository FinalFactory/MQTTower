namespace MQTTower.Core.Models;

public sealed class AclEntry
{
    public string TopicPattern { get; set; } = string.Empty;
    public AclType AclType { get; set; }
    public bool Allow { get; set; } = true;
    public int Priority { get; set; }
}
