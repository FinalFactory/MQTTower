using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTower.Integration.Tests.Fixtures;
using MQTTower.Infrastructure.Mqtt;

namespace MQTTower.Integration.Tests;

[Trait("Category", "Integration")]
[Collection("Mosquitto")]
public sealed class BrokerStatsCollectorIntegrationTests(MosquittoFixture fixture)
{
    [Fact]
    public async Task Sys_tree_updates_connected_clients()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var stats = new BrokerStatsCollector(NullLogger<BrokerStatsCollector>.Instance);
        await stats.AttachAsync(mqtt, CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && stats.GetCurrent().ConnectedClients <= 0)
        {
            await Task.Delay(200);
        }

        stats.GetCurrent().ConnectedClients.Should().BeGreaterThan(0);
    }
}
