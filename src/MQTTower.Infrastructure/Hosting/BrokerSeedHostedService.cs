using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTower.Core;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Hosting;

public sealed class BrokerSeedHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BrokerSeedHostedService> _logger;

    public BrokerSeedHostedService(IServiceScopeFactory scopeFactory, ILogger<BrokerSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.BrokerProfiles.AnyAsync(x => x.Id == BrokerConstants.DefaultLocalBrokerId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        db.BrokerProfiles.Add(new BrokerProfileEntity
        {
            Id = BrokerConstants.DefaultLocalBrokerId,
            Name = "Local",
            AgentUrl = string.Empty,
            ApiKey = string.Empty,
            Status = (int)BrokerStatus.Unknown,
            RegisteredAt = DateTimeOffset.UtcNow,
            Approved = true,
            UseLocalServices = true,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Seeded default local broker profile");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
