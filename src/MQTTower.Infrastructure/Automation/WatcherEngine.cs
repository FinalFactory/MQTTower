using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Automation;

public sealed class WatcherEngine : IWatcherService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMqttSubscriber _subscriber;

    public WatcherEngine(IServiceScopeFactory scopeFactory, IMqttSubscriber subscriber)
    {
        _scopeFactory = scopeFactory;
        _subscriber = subscriber;
    }

    public void Attach(CancellationToken cancellationToken)
    {
        _ = _subscriber.SubscribeAsync("#", OnMessageAsync, cancellationToken);
    }

    private async Task OnMessageAsync(Core.Mqtt.MqttAppMessage msg)
    {
        var text = System.Text.Encoding.UTF8.GetString(msg.Payload);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var router = scope.ServiceProvider.GetRequiredService<INotificationRouter>();
        var watchers = await db.TopicWatchers.AsNoTracking().Where(w => w.Enabled).ToListAsync().ConfigureAwait(false);
        foreach (var w in watchers)
        {
            if (!msg.Topic.StartsWith(w.TopicPattern, StringComparison.OrdinalIgnoreCase) && !TopicMatches(msg.Topic, w.TopicPattern))
            {
                continue;
            }

            if (!Evaluate(w.Condition, text))
            {
                continue;
            }

            await router.DispatchAsync("watcher", JsonSerializer.Serialize(new { w.Name, msg.Topic, text })).ConfigureAwait(false);
        }
    }

    private static bool TopicMatches(string topic, string pattern)
    {
        return topic.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Evaluate(string condition, string payload)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        return payload.Contains(condition, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<TopicWatcher>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.TopicWatchers.AsNoTracking().OrderBy(w => w.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new TopicWatcher
        {
            Id = r.Id,
            Name = r.Name,
            TopicPattern = r.TopicPattern,
            Condition = r.Condition,
            ActionType = r.ActionType,
            ActionConfigJson = r.ActionConfigJson,
            Enabled = r.Enabled,
        }).ToList();
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
            row.TopicPattern = watcher.TopicPattern;
            row.Condition = watcher.Condition;
            row.ActionType = watcher.ActionType;
            row.ActionConfigJson = watcher.ActionConfigJson;
            row.Enabled = watcher.Enabled;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.TopicWatchers.Where(w => w.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
