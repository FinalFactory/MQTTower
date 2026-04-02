using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IDeviceRegistry
{
    Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default);
    Task<Device?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(Device device, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
