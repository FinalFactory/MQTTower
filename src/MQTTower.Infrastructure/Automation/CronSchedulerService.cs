using System.Collections.Concurrent;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Automation;

public sealed class CronSchedulerService : BackgroundService, ISchedulerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CronSchedulerService> _logger;
    private readonly ConcurrentDictionary<Guid, CronExpression> _cronCache = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _nextUtc = new();

    public CronSchedulerService(IServiceScopeFactory scopeFactory, ILogger<CronSchedulerService> logger)
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
                _logger.LogError(ex, "Scheduler tick");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IMqttPublisher>();
        var now = DateTime.UtcNow;
        var tasks = await db.ScheduledTasks.AsNoTracking().Where(t => t.Enabled).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var t in tasks)
        {
            var cron = _cronCache.GetOrAdd(t.Id, _ => CronExpression.Parse(t.CronExpression, CronFormat.Standard));
            var next = _nextUtc.GetOrAdd(t.Id, _ => cron.GetNextOccurrence(now, TimeZoneInfo.Utc, inclusive: false) ?? now);
            if (now < next)
            {
                continue;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(t.Payload);
            await publisher.PublishAsync(t.Topic, bytes, t.Qos, t.Retain, cancellationToken).ConfigureAwait(false);
            var following = cron.GetNextOccurrence(now.AddSeconds(1), TimeZoneInfo.Utc, inclusive: false) ?? now.AddMinutes(1);
            _nextUtc[t.Id] = following;
        }
    }

    public async Task<IReadOnlyList<ScheduledTask>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.ScheduledTasks.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task UpsertAsync(ScheduledTask task, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = task.Id == Guid.Empty ? Guid.NewGuid() : task.Id;
        var row = await db.ScheduledTasks.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            db.ScheduledTasks.Add(new ScheduledTaskEntity
            {
                Id = id,
                Name = task.Name,
                CronExpression = task.CronExpression,
                Topic = task.Topic,
                Payload = task.Payload,
                Qos = task.Qos,
                Retain = task.Retain,
                Enabled = task.Enabled,
            });
        }
        else
        {
            row.Name = task.Name;
            row.CronExpression = task.CronExpression;
            row.Topic = task.Topic;
            row.Payload = task.Payload;
            row.Qos = task.Qos;
            row.Retain = task.Retain;
            row.Enabled = task.Enabled;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _cronCache[id] = CronExpression.Parse(task.CronExpression, CronFormat.Standard);
        _nextUtc[id] = DateTime.MinValue;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ScheduledTasks.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        _cronCache.TryRemove(id, out _);
        _nextUtc.TryRemove(id, out _);
    }

    private static ScheduledTask Map(ScheduledTaskEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        CronExpression = e.CronExpression,
        Topic = e.Topic,
        Payload = e.Payload,
        Qos = e.Qos,
        Retain = e.Retain,
        Enabled = e.Enabled,
    };
}
