namespace MQTTower.Infrastructure.Data.Entities;

public sealed class AuditEntryEntity
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? Details { get; set; }
}
