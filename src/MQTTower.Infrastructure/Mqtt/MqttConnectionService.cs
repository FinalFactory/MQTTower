using System.Collections.Concurrent;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Mqtt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Mqtt;

public sealed class MqttConnectionService : IMqttPublisher, IMqttSubscriber, IAsyncDisposable
{
    private readonly ILogger<MqttConnectionService> _logger;
    private readonly MqttTowerOptions _options;
    private readonly IMqttClient _client = new MqttFactory().CreateMqttClient();
    private readonly ConcurrentDictionary<string, List<Func<MqttAppMessage, Task>>> _handlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    public MqttConnectionService(IOptions<MqttTowerOptions> options, ILogger<MqttConnectionService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }

            var clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(_options.BrokerHost, _options.BrokerPort)
                .WithCredentials(_options.BrokerUsername, _options.BrokerPassword)
                .WithClientId($"mqttower-{Environment.MachineName}-{Guid.NewGuid():N}"[..24])
                .WithCleanSession()
                .Build();

            await _client.ConnectAsync(clientOptions, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("MQTT client connected to {Host}:{Port}", _options.BrokerHost, _options.BrokerPort);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var msg = new MqttAppMessage
        {
            Topic = e.ApplicationMessage.Topic,
            Payload = e.ApplicationMessage.PayloadSegment.ToArray(),
            QoS = (int)e.ApplicationMessage.QualityOfServiceLevel,
            Retain = e.ApplicationMessage.Retain,
        };

        foreach (var kv in _handlers)
        {
            if (!TopicMatches(e.ApplicationMessage.Topic, kv.Key))
            {
                continue;
            }

            List<Func<MqttAppMessage, Task>> snapshot;
            lock (kv.Value)
            {
                snapshot = kv.Value.ToList();
            }

            foreach (var h in snapshot)
            {
                try
                {
                    await h(msg).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Handler error for topic {Topic}", e.ApplicationMessage.Topic);
                }
            }
        }
    }

    private static bool TopicMatches(string topic, string filter)
    {
        if (filter == "#")
        {
            return true;
        }

        var tf = filter.Split('/');
        var tt = topic.Split('/');

        for (var i = 0; i < tf.Length; i++)
        {
            if (tf[i] == "#")
            {
                return true;
            }

            if (i >= tt.Length)
            {
                return false;
            }

            if (tf[i] != "+" && tf[i] != tt[i])
            {
                return false;
            }
        }

        return tf.Length == tt.Length;
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qos = 0, bool retain = false, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var builder = new MQTTnet.MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qos)
            .WithRetainFlag(retain);

        await _client.PublishAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }

        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SubscribeAsync(string topicFilter, Func<MqttAppMessage, Task> handler, CancellationToken cancellationToken = default)
    {
        _handlers.AddOrUpdate(topicFilter, _ => new List<Func<MqttAppMessage, Task>> { handler }, (_, list) =>
        {
            lock (list)
            {
                list.Add(handler);
            }

            return list;
        });

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(topicFilter).Build(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }

        _client.Dispose();
        _connectGate.Dispose();
    }
}
