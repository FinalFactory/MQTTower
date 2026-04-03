using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface ISchedulerService
{
    /// <param name="brokerId">When set, only tasks for this broker; when null, all tasks.</param>
    Task<IReadOnlyList<ScheduledTask>> ListAsync(Guid? brokerId = null, CancellationToken cancellationToken = default);
    Task UpsertAsync(ScheduledTask task, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
