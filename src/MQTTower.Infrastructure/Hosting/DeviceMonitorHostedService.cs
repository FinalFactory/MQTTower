using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;

namespace MQTTower.Infrastructure.Hosting;

public sealed class DeviceMonitorHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DeviceMonitorHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracker = scope.ServiceProvider.GetRequiredService<IDeviceStateTracker>();
            var devices = await db.Devices.AsNoTracking().ToListAsync(stoppingToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            foreach (var d in devices)
            {
                var state = await tracker.GetStateAsync(d.Id, stoppingToken).ConfigureAwait(false);
                if (state is null)
                {
                    continue;
                }

                var missed = now - state.LastSeen > TimeSpan.FromSeconds(d.ExpectedHeartbeatSeconds * 2);
                if (missed && state.Online)
                {
                    await tracker.UpsertAsync(new DeviceState
                    {
                        DeviceId = d.Id,
                        Online = false,
                        LastSeen = state.LastSeen,
                        LastPayloadPreview = state.LastPayloadPreview,
                    }, stoppingToken).ConfigureAwait(false);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }
}
