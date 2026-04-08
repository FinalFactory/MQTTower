using FluentAssertions;
using MQTTower.Integration.Tests.Fixtures;
using MQTTower.Infrastructure.Mqtt;
namespace MQTTower.Integration.Tests;

[Trait("Category", "Integration")]
[Collection("Mosquitto")]
public sealed class MqttConnectionTests(MosquittoFixture fixture)
{
    [Fact]
    public async Task Connect_with_valid_credentials_succeeds()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        mqtt.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Connect_with_wrong_password_fails()
    {
        var bad = fixture.CreateOptions(o => o.BrokerPassword = "wrong-password-12345");
        await using var mqtt = fixture.CreateConnection(bad);
        var act = async () => await mqtt.StartAsync();
        await act.Should().ThrowAsync<Exception>();
        mqtt.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Publish_subscribe_roundtrip_delivers_payload()
    {
        var topic = $"it/roundtrip/{Guid.NewGuid():N}";
        await using var subscriber = fixture.CreateConnection();
        await subscriber.StartAsync();

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await subscriber.SubscribeAsync(
            topic,
            m =>
            {
                tcs.TrySetResult(System.Text.Encoding.UTF8.GetString(m.Payload));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Task.Delay(300);

        await using var publisher = fixture.CreateConnection();
        await publisher.StartAsync();
        var payload = System.Text.Encoding.UTF8.GetBytes("hello-mqtt");
        await publisher.PublishAsync(topic, payload, qos: 1, retain: false, CancellationToken.None);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.Should().Be("hello-mqtt");
    }

    [Fact]
    public async Task Qos1_publish_is_received()
    {
        var topic = $"it/qos1/{Guid.NewGuid():N}";
        await using var subscriber = fixture.CreateConnection();
        await subscriber.StartAsync();

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await subscriber.SubscribeAsync(
            topic,
            m =>
            {
                tcs.TrySetResult(m.QoS);
                return Task.CompletedTask;
            },
            subscriptionQos: 1,
            CancellationToken.None);

        await Task.Delay(300);

        await using var publisher = fixture.CreateConnection();
        await publisher.StartAsync();
        await publisher.PublishAsync(topic, new byte[] { 1 }, qos: 1, retain: false, CancellationToken.None);
        var qos = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        qos.Should().Be(1);
    }

    [Fact]
    public async Task Retained_message_delivered_to_new_subscriber()
    {
        var topic = $"it/retain/{Guid.NewGuid():N}";
        await using var publisher = fixture.CreateConnection();
        await publisher.StartAsync();
        await publisher.PublishAsync(topic, System.Text.Encoding.UTF8.GetBytes("retained-body"), qos: 1, retain: true, CancellationToken.None);
        await publisher.DisposeAsync();

        await using var subscriber = fixture.CreateConnection();
        await subscriber.StartAsync();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await subscriber.SubscribeAsync(
            topic,
            m =>
            {
                tcs.TrySetResult(System.Text.Encoding.UTF8.GetString(m.Payload));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Task.Delay(300);

        var body = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        body.Should().Be("retained-body");

        // Clear retain on topic for broker hygiene
        await subscriber.PublishAsync(topic, Array.Empty<byte>(), qos: 1, retain: true, CancellationToken.None);
    }
}
