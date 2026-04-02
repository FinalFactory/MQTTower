using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface ISchedulerService
{
    Task<IReadOnlyList<ScheduledTask>> ListAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(ScheduledTask task, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
