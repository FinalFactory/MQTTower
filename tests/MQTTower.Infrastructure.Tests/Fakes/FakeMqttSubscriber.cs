using MQTTower.Core.Interfaces;
using MQTTower.Core.Mqtt;

namespace MQTTower.Infrastructure.Tests.Fakes;

public sealed class FakeMqttSubscriber : IMqttSubscriber
{
    public Dictionary<string, Func<MqttAppMessage, Task>> Handlers { get; } = new(StringComparer.Ordinal);

    public Task SubscribeAsync(string topicFilter, Func<MqttAppMessage, Task> handler, CancellationToken cancellationToken = default)
    {
        Handlers[topicFilter] = handler;
        return Task.CompletedTask;
    }
}
