namespace MQTTower.Infrastructure.Data.Entities;

public sealed class ScheduledTaskEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public int Qos { get; set; }
    public bool Retain { get; set; }
    public bool Enabled { get; set; } = true;
}
