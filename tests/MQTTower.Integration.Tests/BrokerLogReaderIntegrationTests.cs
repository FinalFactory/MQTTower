using FluentAssertions;
using Microsoft.Extensions.Options;
using MQTTower.Integration.Tests.Fixtures;
using MQTTower.Infrastructure.Monitoring;

namespace MQTTower.Integration.Tests;

[Trait("Category", "Integration")]
[Collection("Mosquitto")]
public sealed class BrokerLogReaderIntegrationTests(MosquittoFixture fixture)
{
    [Fact]
    public async Task GetRecentLinesAsync_returns_log_lines_after_mqtt_activity()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        await mqtt.PublishAsync($"it/log/{Guid.NewGuid():N}/ping", new byte[] { 1 }, qos: 0, retain: false, CancellationToken.None);
        await Task.Delay(1500);

        var reader = new BrokerLogReader(Options.Create(fixture.CreateOptions()));
        var lines = await reader.GetRecentLinesAsync(80);
        lines.Should().NotBeEmpty();
        var joined = string.Join('\n', lines);
        joined.ToLowerInvariant().Should().Contain("mosquitto");
    }
}
