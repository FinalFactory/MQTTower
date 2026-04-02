namespace MQTTower.Infrastructure.Data.Entities;

public sealed class DeviceStateEntity
{
    public Guid DeviceId { get; set; }
    public bool Online { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public string? LastPayloadPreview { get; set; }
}
