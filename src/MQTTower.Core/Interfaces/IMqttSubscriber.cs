using MQTTower.Core.Mqtt;

namespace MQTTower.Core.Interfaces;

public interface IMqttSubscriber
{
    Task SubscribeAsync(string topicFilter, Func<MqttAppMessage, Task> handler, CancellationToken cancellationToken = default);
}
