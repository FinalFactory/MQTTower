namespace MQTTower.Core.Models;

public sealed class MetricSnapshot
{
    public long Id { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
}
