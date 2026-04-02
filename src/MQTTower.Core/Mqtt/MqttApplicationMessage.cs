namespace MQTTower.Core.Mqtt;

public sealed class MqttAppMessage
{
    public string Topic { get; init; } = string.Empty;
    public byte[] Payload { get; init; } = Array.Empty<byte>();
    public string? ContentType { get; init; }
    public int QoS { get; init; }
    public bool Retain { get; init; }
}
