using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IMetricStore
{
    Task AppendAsync(MetricSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MetricSnapshot>> QueryAsync(string name, DateTimeOffset from, DateTimeOffset to, Guid? brokerId = null, CancellationToken cancellationToken = default);
    Task PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
