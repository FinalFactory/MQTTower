using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Agents;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Automation;

/// <summary>Dashboard-side watcher CRUD backed by SQLite; syncs definitions to the broker agent over HTTP.</summary>
public sealed class DashboardWatcherService : IWatcherService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBrokerRegistry _registry;
    private readonly IBrokerGatewayFactory _gatewayFactory;
    private readonly ILogger<DashboardWatcherService> _logger;

    public DashboardWatcherService(
        IServiceScopeFactory scopeFactory,
        IBrokerRegistry registry,
        IBrokerGatewayFactory gatewayFactory,
        ILogger<DashboardWatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _gatewayFactory = gatewayFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TopicWatcher>> ListAsync(Guid? brokerId = null, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var q = db.TopicWatchers.AsNoTracking().AsQueryable();
        if (brokerId.HasValue)
        {
            q = q.Where(w => w.BrokerId == brokerId.Value);
        }

        var rows = await q.OrderBy(w => w.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToModel).ToList();
    }

    public async Task UpsertAsync(TopicWatcher watcher, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TopicWatchers.FindAsync(new object[] { watcher.Id }, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            db.TopicWatchers.Add(new TopicWatcherEntity
            {
                Id = watcher.Id == Guid.Empty ? Guid.NewGuid() : watcher.Id,
                BrokerId = watcher.BrokerId,
                Name = watcher.Name,
                TopicPattern = watcher.TopicPattern,
                Condition = watcher.Condition,
                ActionType = watcher.ActionType,
                ActionConfigJson = watcher.ActionConfigJson,
                Enabled = watcher.Enabled,
            });
        }
        else
        {
            row.Name = watcher.Name;
            row.BrokerId = watcher.BrokerId;
            row.TopicPattern = watcher.TopicPattern;
            row.Condition = watcher.Condition;
            row.ActionType = watcher.ActionType;
            row.ActionConfigJson = watcher.ActionConfigJson;
            row.Enabled = watcher.Enabled;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await SyncBrokerAsync(watcher.BrokerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Guid? brokerId;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.TopicWatchers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, cancellationToken).ConfigureAwait(false);
            brokerId = row?.BrokerId;
            await db.TopicWatchers.Where(w => w.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        await SyncBrokerAsync(brokerId, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncBrokerAsync(Guid? brokerId, CancellationToken cancellationToken)
    {
        if (brokerId is null)
        {
            return;
        }

        var broker = await _registry.GetAsync(brokerId.Value, cancellationToken).ConfigureAwait(false);
        if (broker is null || string.IsNullOrWhiteSpace(broker.AgentUrl))
        {
            return;
        }

        var watchers = (await ListAsync(brokerId, cancellationToken).ConfigureAwait(false)).ToList();
        IBrokerGateway? gw = null;
        try
        {
            gw = _gatewayFactory.Create(broker);
            if (gw is AgentHttpClient http)
            {
                var resp = await http.SyncWatchersAsync(watchers, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Agent watcher sync failed with status {Status}", (int)resp.StatusCode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent watcher sync failed for broker {BrokerId}", brokerId);
        }
        finally
        {
            (gw as IDisposable)?.Dispose();
        }
    }

    private static TopicWatcher ToModel(TopicWatcherEntity r) => new()
    {
        Id = r.Id,
        BrokerId = r.BrokerId,
        Name = r.Name,
        TopicPattern = r.TopicPattern,
        Condition = r.Condition,
        ActionType = r.ActionType,
        ActionConfigJson = r.ActionConfigJson,
        Enabled = r.Enabled,
    };
}
