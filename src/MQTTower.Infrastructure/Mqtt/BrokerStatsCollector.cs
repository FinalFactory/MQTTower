using System.Globalization;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MqttAppMessage = MQTTower.Core.Mqtt.MqttAppMessage;
using Microsoft.Extensions.Logging;

namespace MQTTower.Infrastructure.Mqtt;

public sealed class BrokerStatsCollector : IBrokerStatsProvider
{
    private readonly ILogger<BrokerStatsCollector> _logger;
    private readonly object _sync = new();
    private BrokerStats _stats = new();
    private DateTimeOffset _lastSample = DateTimeOffset.UtcNow;
    private double _lastMessageCount;

    public BrokerStatsCollector(ILogger<BrokerStatsCollector> logger)
    {
        _logger = logger;
    }

    public void Attach(IMqttSubscriber subscriber, CancellationToken cancellationToken)
    {
        _ = subscriber.SubscribeAsync("$SYS/#", OnSysMessageAsync, cancellationToken);
    }

    private Task OnSysMessageAsync(MqttAppMessage msg)
    {
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(msg.Payload);
            lock (_sync)
            {
                if (msg.Topic.EndsWith("/clients/connected", StringComparison.OrdinalIgnoreCase) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var connected))
                {
                    _stats.ConnectedClients = connected;
                }
                else if (msg.Topic.Contains("load/messages/received", StringComparison.OrdinalIgnoreCase) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var mps))
                {
                    _stats.MessagesPerSecond = mps;
                }
                else if (msg.Topic.Contains("load/bytes/received", StringComparison.OrdinalIgnoreCase) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bps))
                {
                    _stats.DataRateBytesPerSecond = bps;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "SYS parse");
        }

        return Task.CompletedTask;
    }

    public BrokerStats GetCurrent()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - _lastSample).TotalSeconds;
            if (elapsed > 1)
            {
                _stats.ConnectedClientsDeltaThisHour = 0;
                _stats.MessagesPerSecondDeltaPercent = 0;
                _lastSample = now;
                _lastMessageCount = _stats.MessagesPerSecond;
            }

            _stats.ActiveTopics = Math.Max(_stats.ActiveTopics, _stats.ConnectedClients * 4);
            return new BrokerStats
            {
                ConnectedClients = _stats.ConnectedClients,
                MessagesPerSecond = _stats.MessagesPerSecond,
                ActiveTopics = _stats.ActiveTopics,
                DataRateBytesPerSecond = _stats.DataRateBytesPerSecond,
                Uptime = _stats.Uptime,
                ConnectedClientsDeltaThisHour = _stats.ConnectedClientsDeltaThisHour,
                MessagesPerSecondDeltaPercent = _stats.MessagesPerSecondDeltaPercent,
            };
        }
    }
}
