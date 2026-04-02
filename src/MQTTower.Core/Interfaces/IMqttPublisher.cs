namespace MQTTower.Core.Interfaces;

public interface IMqttPublisher
{
    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qos = 0, bool retain = false, CancellationToken cancellationToken = default);
}
