using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Hosting;

public sealed class MetricsCollectorHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBrokerStatsProvider _stats;
    private readonly MqttTowerOptions _options;

    public MetricsCollectorHostedService(IServiceScopeFactory scopeFactory, IBrokerStatsProvider stats, IOptions<MqttTowerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _stats = stats;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var metrics = scope.ServiceProvider.GetRequiredService<IMetricStore>();
            var s = _stats.GetCurrent();
            await metrics.AppendAsync(new MetricSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                Name = "messagesPerSecond",
                Value = s.MessagesPerSecond,
            }, stoppingToken).ConfigureAwait(false);

            await metrics.AppendAsync(new MetricSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                Name = "connectedClients",
                Value = s.ConnectedClients,
            }, stoppingToken).ConfigureAwait(false);

            var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.MetricsRetentionDays);
            await metrics.PruneOlderThanAsync(cutoff, stoppingToken).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
