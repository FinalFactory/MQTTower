using MQTTower.Core.Mqtt;

namespace MQTTower.Core.Interfaces;

public interface IMqttSubscriber
{
    Task SubscribeAsync(string topicFilter, Func<MqttAppMessage, Task> handler, CancellationToken cancellationToken = default);

    /// <param name="subscriptionQos">Broker grant level (0–2). Effective delivery QoS is min(publish QoS, subscription QoS).</param>
    Task SubscribeAsync(string topicFilter, Func<MqttAppMessage, Task> handler, int subscriptionQos, CancellationToken cancellationToken = default);
}
