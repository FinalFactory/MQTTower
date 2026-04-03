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
    private readonly ConcurrentDictionary<string, byte> _brokerSubscribedFilters = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private volatile bool _disposed;

    public MqttConnectionService(IOptions<MqttTowerOptions> options, ILogger<MqttConnectionService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        _client.DisconnectedAsync += _ =>
        {
            _brokerSubscribedFilters.Clear();
            return Task.CompletedTask;
        };
    }

    public bool IsConnected => _client.IsConnected;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

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
            if (!MqttTopicMatcher.TopicMatches(e.ApplicationMessage.Topic, kv.Key))
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
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MqttConnectionService));
        }

        if (_client.IsConnected)
        {
            return;
        }

        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SubscribeAsync(string topicFilter, Func<MqttAppMessage, Task> handler, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MqttConnectionService));
        }

        _handlers.AddOrUpdate(topicFilter, _ => new List<Func<MqttAppMessage, Task>> { handler }, (_, list) =>
        {
            lock (list)
            {
                list.Add(handler);
            }

            return list;
        });

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        if (_brokerSubscribedFilters.TryAdd(topicFilter, 0))
        {
            await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(topicFilter).Build(), cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _connectGate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            _disposed = true;
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }

            _client.Dispose();
        }
        finally
        {
            try
            {
                _connectGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // gate already disposed
            }
        }

        try
        {
            _connectGate.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
