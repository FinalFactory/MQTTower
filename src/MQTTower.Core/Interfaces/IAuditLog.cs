using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IAuditLog
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(DateTimeOffset? from, DateTimeOffset? to, string? userName, int skip, int take, CancellationToken cancellationToken = default);
}
