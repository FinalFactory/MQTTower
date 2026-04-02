using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IDeviceStateTracker
{
    Task<DeviceState?> GetStateAsync(Guid deviceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, DeviceState>> GetAllStatesAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(DeviceState state, CancellationToken cancellationToken = default);
}
