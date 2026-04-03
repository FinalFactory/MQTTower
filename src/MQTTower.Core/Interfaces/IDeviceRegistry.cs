using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IDeviceRegistry
{
    /// <param name="brokerId">When set, only devices for this broker; when null, all devices.</param>
    Task<IReadOnlyList<Device>> ListAsync(Guid? brokerId = null, CancellationToken cancellationToken = default);
    Task<Device?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(Device device, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
