using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Monitoring;

public sealed class EfDeviceStateTracker : IDeviceStateTracker
{
    private readonly AppDbContext _db;

    public EfDeviceStateTracker(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DeviceState?> GetStateAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var row = await _db.DeviceStates.AsNoTracking().FirstOrDefaultAsync(s => s.DeviceId == deviceId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyDictionary<Guid, DeviceState>> GetAllStatesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.DeviceStates.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.DeviceId, r => Map(r));
    }

    public async Task UpsertAsync(DeviceState state, CancellationToken cancellationToken = default)
    {
        var row = await _db.DeviceStates.FindAsync(new object[] { state.DeviceId }, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            _db.DeviceStates.Add(Map(state));
        }
        else
        {
            row.Online = state.Online;
            row.LastSeen = state.LastSeen;
            row.LastPayloadPreview = state.LastPayloadPreview;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DeviceState Map(DeviceStateEntity e) => new()
    {
        DeviceId = e.DeviceId,
        Online = e.Online,
        LastSeen = e.LastSeen,
        LastPayloadPreview = e.LastPayloadPreview,
    };

    private static DeviceStateEntity Map(DeviceState s) => new()
    {
        DeviceId = s.DeviceId,
        Online = s.Online,
        LastSeen = s.LastSeen,
        LastPayloadPreview = s.LastPayloadPreview,
    };
}
