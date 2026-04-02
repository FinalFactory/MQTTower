using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IWatcherService
{
    Task<IReadOnlyList<TopicWatcher>> ListAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(TopicWatcher watcher, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
