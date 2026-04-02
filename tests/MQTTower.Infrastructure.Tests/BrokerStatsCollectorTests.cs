using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTower.Core.Mqtt;
using MQTTower.Infrastructure.Mqtt;
using MQTTower.Infrastructure.Tests.Fakes;

namespace MQTTower.Infrastructure.Tests;

public sealed class BrokerStatsCollectorTests
{
    [Fact]
    public async Task Parses_sys_messages_into_stats()
    {
        var collector = new BrokerStatsCollector(NullLogger<BrokerStatsCollector>.Instance);
        var sub = new FakeMqttSubscriber();
        collector.Attach(sub, CancellationToken.None);

        var handler = sub.Handlers["$SYS/#"];
        await handler(new MqttAppMessage
        {
            Topic = "$SYS/broker/clients/connected",
            Payload = Encoding.UTF8.GetBytes("7"),
        });
        await handler(new MqttAppMessage
        {
            Topic = "$SYS/broker/load/messages/received/1",
            Payload = Encoding.UTF8.GetBytes("3.5"),
        });

        var s = collector.GetCurrent();
        s.ConnectedClients.Should().Be(7);
        s.MessagesPerSecond.Should().Be(3.5);
    }
}
