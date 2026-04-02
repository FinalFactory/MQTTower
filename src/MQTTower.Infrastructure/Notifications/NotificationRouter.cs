using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Notifications;

public sealed class NotificationRouter : INotificationRouter
{
    private readonly AppDbContext _db;
    private readonly IEnumerable<INotificationChannel> _channels;

    public NotificationRouter(AppDbContext db, IEnumerable<INotificationChannel> channels)
    {
        _db = db;
        _channels = channels;
    }

    public async Task DispatchAsync(string triggerType, string payloadJson, CancellationToken cancellationToken = default)
    {
        var rules = await _db.NotificationRules.AsNoTracking()
            .Where(r => r.Enabled && r.TriggerType == triggerType)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var rule in rules)
        {
            var channel = _channels.FirstOrDefault(c => c.ChannelId == rule.Channel);
            if (channel is null)
            {
                continue;
            }

            await channel.SendAsync(rule.Name, payloadJson, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<NotificationRule>> ListRulesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.NotificationRules.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new NotificationRule
        {
            Id = r.Id,
            Name = r.Name,
            TriggerType = r.TriggerType,
            ConfigJson = r.ConfigJson,
            Channel = r.Channel,
            Enabled = r.Enabled,
        }).ToList();
    }

    public async Task UpsertRuleAsync(NotificationRule rule, CancellationToken cancellationToken = default)
    {
        var row = await _db.NotificationRules.FindAsync(new object[] { rule.Id }, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            _db.NotificationRules.Add(new NotificationRuleEntity
            {
                Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
                Name = rule.Name,
                TriggerType = rule.TriggerType,
                ConfigJson = rule.ConfigJson,
                Channel = rule.Channel,
                Enabled = rule.Enabled,
            });
        }
        else
        {
            row.Name = rule.Name;
            row.TriggerType = rule.TriggerType;
            row.ConfigJson = rule.ConfigJson;
            row.Channel = rule.Channel;
            row.Enabled = rule.Enabled;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _db.NotificationRules.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
