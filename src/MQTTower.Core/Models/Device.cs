namespace MQTTower.Core.Models;

public sealed class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Location { get; set; }
    public string? Firmware { get; set; }
    public string? IpAddress { get; set; }
    public string? Notes { get; set; }
    public string? GroupName { get; set; }
    public int ExpectedHeartbeatSeconds { get; set; } = 300;
}
