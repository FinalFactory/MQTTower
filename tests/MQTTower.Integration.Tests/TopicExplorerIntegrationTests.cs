using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTower.Integration.Tests.Fixtures;
using MQTTower.Infrastructure.Monitoring;
using MQTTower.Infrastructure.Mqtt;
using System.Text;

namespace MQTTower.Integration.Tests;

[Trait("Category", "Integration")]
[Collection("Mosquitto")]
public sealed class TopicExplorerIntegrationTests(MosquittoFixture fixture)
{
    [Fact]
    public async Task Publishes_build_topic_tree_and_message_counts()
    {
        await using var subscriber = fixture.CreateConnection();
        await subscriber.StartAsync();
        var topics = new TopicExplorerService(Options.Create(fixture.CreateOptions()), NullLogger<TopicExplorerService>.Instance);
        await topics.AttachAsync(subscriber, CancellationToken.None);

        var id = Guid.NewGuid().ToString("N");
        var prefix = $"it/te/{id}";
        await using var publisher = fixture.CreateConnection();
        await publisher.StartAsync();
        await publisher.PublishAsync($"{prefix}/living/temp", Encoding.UTF8.GetBytes("22.1"), qos: 1, retain: false, CancellationToken.None);
        await publisher.PublishAsync($"{prefix}/kitchen/humidity", Encoding.UTF8.GetBytes("40"), qos: 1, retain: false, CancellationToken.None);
        await publisher.PublishAsync($"{prefix}/living/temp", Encoding.UTF8.GetBytes("22.5"), qos: 1, retain: false, CancellationToken.None);

        await Task.Delay(800);
        var roots = topics.GetRoots();
        var it = roots.Should().ContainSingle(n => n.Segment == "it").Which;
        var te = it.Children.Should().ContainSingle(c => c.Segment == "te").Which;
        var idNode = te.Children.Should().ContainSingle(c => c.Segment == id).Which;
        var living = idNode.Children.Should().ContainSingle(c => c.Segment == "living").Which;
        var temp = living.Children.Should().ContainSingle(c => c.Segment == "temp").Which;
        temp.MessageCount.Should().BeGreaterThanOrEqualTo(2);
        temp.LastPayloadPreview.Should().Contain("22");
    }
}
