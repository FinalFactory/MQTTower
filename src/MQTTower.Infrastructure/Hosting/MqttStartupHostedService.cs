using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTower.Core.Interfaces;
using MQTTower.Infrastructure.Monitoring;
using MQTTower.Infrastructure.Mqtt;

namespace MQTTower.Infrastructure.Hosting;

public sealed class MqttStartupHostedService : IHostedService
{
    private readonly MqttConnectionService _mqtt;
    private readonly BrokerStatsCollector _stats;
    private readonly TopicExplorerService _topics;
    private readonly ILogger<MqttStartupHostedService> _logger;

    public MqttStartupHostedService(
        MqttConnectionService mqtt,
        BrokerStatsCollector stats,
        TopicExplorerService topics,
        ILogger<MqttStartupHostedService> logger)
    {
        _mqtt = mqtt;
        _stats = stats;
        _topics = topics;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _mqtt.StartAsync(cancellationToken).ConfigureAwait(false);
            _stats.Attach(_mqtt, cancellationToken);
            _topics.Attach(_mqtt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT broker not reachable; UI will work in degraded mode");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
