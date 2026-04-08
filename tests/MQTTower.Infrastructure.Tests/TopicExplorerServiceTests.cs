using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTower.Core.Mqtt;
using MQTTower.Infrastructure.Monitoring;
using MQTTower.Infrastructure.Options;
using MQTTower.Infrastructure.Tests.Fakes;

namespace MQTTower.Infrastructure.Tests;

public sealed class TopicExplorerServiceTests
{
    [Fact]
    public async Task Builds_tree_from_messages()
    {
        var svc = new TopicExplorerService(Microsoft.Extensions.Options.Options.Create(new MqttTowerOptions()), NullLogger<TopicExplorerService>.Instance);
        var sub = new FakeMqttSubscriber();
        await svc.AttachAsync(sub, CancellationToken.None);
        var handler = sub.Handlers["#"];
        await handler(new MqttAppMessage { Topic = "home/living/temp", Payload = Encoding.UTF8.GetBytes("21") });

        var roots = svc.GetRoots();
        roots.Should().ContainSingle();
        roots[0].Segment.Should().Be("home");
        roots[0].Children.Should().ContainSingle();
        var living = roots[0].Children[0];
        living.Children.Should().ContainSingle();
        living.Children[0].FullTopic.Should().Be("home/living/temp");
    }
}
