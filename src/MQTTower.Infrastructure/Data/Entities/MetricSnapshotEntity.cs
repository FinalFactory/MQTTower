namespace MQTTower.Infrastructure.Data.Entities;

public sealed class MetricSnapshotEntity
{
    public long Id { get; set; }
    public Guid? BrokerId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
}
