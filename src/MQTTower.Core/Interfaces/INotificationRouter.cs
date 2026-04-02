using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface INotificationRouter
{
    Task DispatchAsync(string triggerType, string payloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationRule>> ListRulesAsync(CancellationToken cancellationToken = default);
    Task UpsertRuleAsync(NotificationRule rule, CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default);
}
