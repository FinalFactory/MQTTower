using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTower.Core;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Hosting;

public sealed class BrokerSeedHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<MqttTowerOptions> _towerOptions;
    private readonly ILogger<BrokerSeedHostedService> _logger;

    public BrokerSeedHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<MqttTowerOptions> towerOptions,
        ILogger<BrokerSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _towerOptions = towerOptions;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _towerOptions.Value;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var localId = BrokerConstants.DefaultLocalBrokerId;
        var row = await db.BrokerProfiles.FirstOrDefaultAsync(x => x.Id == localId, cancellationToken).ConfigureAwait(false);

        var agentUrl = opts.LocalAgentUrl?.Trim() ?? string.Empty;
        var apiKey = opts.LocalAgentApiKey ?? string.Empty;

        if (row is null)
        {
            var initialStatus = string.IsNullOrWhiteSpace(agentUrl) ? BrokerStatus.Offline : BrokerStatus.Unknown;
            db.BrokerProfiles.Add(new BrokerProfileEntity
            {
                Id = localId,
                Name = "Local",
                AgentUrl = agentUrl,
                ApiKey = apiKey,
                Status = (int)initialStatus,
                RegisteredAt = DateTimeOffset.UtcNow,
                Approved = true,
                UseLocalServices = true,
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Seeded default local broker profile");
            return;
        }

        if (!string.IsNullOrWhiteSpace(agentUrl) &&
            (row.AgentUrl != agentUrl || row.ApiKey != apiKey))
        {
            row.AgentUrl = agentUrl;
            row.ApiKey = apiKey;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Updated local broker profile from configuration (LocalAgentUrl)");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
