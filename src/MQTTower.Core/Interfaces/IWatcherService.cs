using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IWatcherService
{
    /// <param name="brokerId">When set, only watchers for this broker; when null, all watchers.</param>
    Task<IReadOnlyList<TopicWatcher>> ListAsync(Guid? brokerId = null, CancellationToken cancellationToken = default);
    Task UpsertAsync(TopicWatcher watcher, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
