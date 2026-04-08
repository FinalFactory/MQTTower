using System.Text;
using FluentAssertions;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using MQTTower.Integration.Tests.Fixtures;

namespace MQTTower.Integration.Tests;

[Trait("Category", "Integration")]
[Collection("Mosquitto")]
public sealed class RawMqttnetSanityTests(MosquittoFixture fixture)
{
    [Fact]
    public async Task Two_clients_publish_subscribe_roundtrip()
    {
        var factory = new MqttFactory();
        var topic = $"it/raw/{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = factory.CreateMqttClient();
        sub.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == topic)
            {
                tcs.TrySetResult(Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment));
            }

            return Task.CompletedTask;
        };

        await sub.ConnectAsync(
            new MqttClientOptionsBuilder()
                .WithProtocolVersion(MqttProtocolVersion.V311)
                .WithTcpServer("127.0.0.1", fixture.MappedMqttPort)
                .WithCredentials(MosquittoFixture.AdminUsername, MosquittoFixture.AdminPassword)
                .WithClientId("raw-sub-" + Guid.NewGuid().ToString("N")[..20])
                .WithCleanSession()
                .Build(),
            CancellationToken.None);

        var subResult = await sub.SubscribeAsync(
            new MqttTopicFilterBuilder().WithTopic("it/#").Build(),
            CancellationToken.None);

        foreach (var item in subResult.Items)
        {
            item.ResultCode.Should().Be(
                MqttClientSubscribeResultCode.GrantedQoS0,
                $"subscribe denied for {item.TopicFilter}: {item.ResultCode}");
        }

        await Task.Delay(500, CancellationToken.None);

        using var pub = factory.CreateMqttClient();
        await pub.ConnectAsync(
            new MqttClientOptionsBuilder()
                .WithProtocolVersion(MqttProtocolVersion.V311)
                .WithTcpServer("127.0.0.1", fixture.MappedMqttPort)
                .WithCredentials(MosquittoFixture.AdminUsername, MosquittoFixture.AdminPassword)
                .WithClientId("raw-pub-" + Guid.NewGuid().ToString("N")[..20])
                .WithCleanSession()
                .Build(),
            CancellationToken.None);

        var pubResult = await pub.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload("ping")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(),
            CancellationToken.None);

        pubResult.IsSuccess.Should().BeTrue($"publish failed: {pubResult.ReasonCode} {pubResult.ReasonString}");

        string received;
        try
        {
            received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        catch (TimeoutException)
        {
            var tail = await ReadLogTailForDebugAsync(fixture.HostLogFilePath, 40);
            throw new TimeoutException("No message; mosquitto.log tail: " + tail);
        }

        received.Should().Be("ping");
    }

    private static async Task<string> ReadLogTailForDebugAsync(string path, int maxLines)
    {
        if (maxLines <= 0 || !File.Exists(path))
        {
            return "(no log)";
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        if (content.Length == 0)
        {
            return "(empty)";
        }

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var take = Math.Min(maxLines, lines.Length);
        return string.Join(Environment.NewLine, lines[^take..]);
    }
}
