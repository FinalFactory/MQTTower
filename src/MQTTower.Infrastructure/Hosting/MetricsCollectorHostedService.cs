using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Hosting;

public sealed class MetricsCollectorHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<MqttTowerOptions> _options;
    private readonly ILogger<MetricsCollectorHostedService> _logger;

    public MetricsCollectorHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<MqttTowerOptions> options,
        ILogger<MetricsCollectorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectMetricsOnceAsync(_scopeFactory, _options.Value, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metrics collection tick failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>One collection + prune cycle; used by <see cref="ExecuteAsync"/> and unit tests.</summary>
    internal static async Task CollectMetricsOnceAsync(
        IServiceScopeFactory scopeFactory,
        MqttTowerOptions options,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var metrics = scope.ServiceProvider.GetRequiredService<IMetricStore>();
        var registry = scope.ServiceProvider.GetRequiredService<IBrokerRegistry>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var factory = scope.ServiceProvider.GetRequiredService<IBrokerGatewayFactory>();

        var brokers = await registry.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var b in brokers.Where(x => x.Approved && !string.IsNullOrWhiteSpace(x.AgentUrl)))
        {
            IBrokerGateway? gw = null;
            try
            {
                gw = factory.Create(b);
                var s = await gw.GetStatsAsync(cancellationToken).ConfigureAwait(false);
                await metrics.AppendAsync(new MetricSnapshot
                {
                    BrokerId = b.Id,
                    CapturedAt = DateTimeOffset.UtcNow,
                    Name = "messagesPerSecond",
                    Value = s.MessagesPerSecond,
                }, cancellationToken).ConfigureAwait(false);

                await metrics.AppendAsync(new MetricSnapshot
                {
                    BrokerId = b.Id,
                    CapturedAt = DateTimeOffset.UtcNow,
                    Name = "connectedClients",
                    Value = s.ConnectedClients,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                /* ignore per-broker failures */
            }
            finally
            {
                (gw as IDisposable)?.Dispose();
            }
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.MetricsRetentionDays);
        await metrics.PruneOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);

        var auditCutoff = DateTimeOffset.UtcNow.AddDays(-options.AuditRetentionDays);
        await audit.PruneOlderThanAsync(auditCutoff, cancellationToken).ConfigureAwait(false);
    }
}
