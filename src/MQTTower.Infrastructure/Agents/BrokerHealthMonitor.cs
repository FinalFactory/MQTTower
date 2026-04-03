using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Infrastructure.Agents;

public sealed class BrokerHealthMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BrokerHealthMonitor> _logger;

    public BrokerHealthMonitor(IServiceScopeFactory scopeFactory, ILogger<BrokerHealthMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Broker health tick");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IBrokerRegistry>();
        var factory = scope.ServiceProvider.GetRequiredService<IBrokerGatewayFactory>();
        var brokers = await registry.ListAsync(ct).ConfigureAwait(false);
        foreach (var b in brokers)
        {
            if (!b.Approved)
            {
                continue;
            }

            if (b.UseLocalServices)
            {
                IBrokerGateway? localGw = null;
                try
                {
                    localGw = factory.Create(b);
                    var info = await localGw.GetHealthAsync(ct).ConfigureAwait(false);
                    b.Status = info.MqttConnected ? BrokerStatus.Online : BrokerStatus.Degraded;
                    b.LastSeen = DateTimeOffset.UtcNow;
                    await registry.UpdateAsync(b, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Local broker health");
                    b.Status = BrokerStatus.Offline;
                    await registry.UpdateAsync(b, ct).ConfigureAwait(false);
                }
                finally
                {
                    (localGw as IDisposable)?.Dispose();
                }

                continue;
            }

            IBrokerGateway? gateway = null;
            try
            {
                gateway = factory.Create(b);
                var info = await gateway.GetHealthAsync(ct).ConfigureAwait(false);
                b.Status = info.MqttConnected ? BrokerStatus.Online : BrokerStatus.Degraded;
                b.LastSeen = DateTimeOffset.UtcNow;
                if (string.IsNullOrEmpty(b.TlsCertThumbprint) && !string.IsNullOrEmpty(info.TlsCertThumbprint))
                {
                    b.TlsCertThumbprint = info.TlsCertThumbprint;
                }

                await registry.UpdateAsync(b, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Remote broker {Id} unreachable", b.Id);
                b.Status = BrokerStatus.Offline;
                await registry.UpdateAsync(b, ct).ConfigureAwait(false);
            }
            finally
            {
                (gateway as IDisposable)?.Dispose();
            }
        }
    }
}
