namespace MQTTower.Infrastructure.Data.Entities;

public sealed class NotificationRuleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public string Channel { get; set; } = "ntfy";
    public bool Enabled { get; set; } = true;
}
