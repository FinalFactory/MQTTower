using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Notifications;

public sealed class NotificationRouter : INotificationRouter
{
    private readonly AppDbContext _db;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationRouter> _logger;

    public NotificationRouter(AppDbContext db, IEnumerable<INotificationChannel> channels, ILogger<NotificationRouter> logger)
    {
        _db = db;
        _channels = channels;
        _logger = logger;
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
                _logger.LogWarning("No notification channel registered for id {ChannelId} (rule {RuleName})", rule.Channel, rule.Name);
                continue;
            }

            const int maxAttempts = 2;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await channel.SendAsync(rule.Name, payloadJson, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Notification send failed for rule {RuleName} channel {Channel} attempt {Attempt}/{Max}", rule.Name, rule.Channel, attempt, maxAttempts);
                    if (attempt == maxAttempts)
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken).ConfigureAwait(false);
                }
            }
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
