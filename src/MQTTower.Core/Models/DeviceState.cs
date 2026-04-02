namespace MQTTower.Core.Models;

public sealed class DeviceState
{
    public Guid DeviceId { get; set; }
    public bool Online { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public string? LastPayloadPreview { get; set; }
}
