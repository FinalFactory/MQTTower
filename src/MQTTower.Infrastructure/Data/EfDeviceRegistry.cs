using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Data;

public sealed class EfDeviceRegistry : IDeviceRegistry
{
    private readonly AppDbContext _db;

    public EfDeviceRegistry(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddOrUpdateAsync(Device device, CancellationToken cancellationToken = default)
    {
        var row = await _db.Devices.FindAsync(device.Id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            _db.Devices.Add(Map(device));
        }
        else
        {
            row.Name = device.Name;
            row.Type = device.Type;
            row.Location = device.Location;
            row.Firmware = device.Firmware;
            row.IpAddress = device.IpAddress;
            row.Notes = device.Notes;
            row.GroupName = device.GroupName;
            row.ExpectedHeartbeatSeconds = device.ExpectedHeartbeatSeconds;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _db.Devices.Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Device?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.Devices.AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    private static DeviceEntity Map(Device d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Type = d.Type,
        Location = d.Location,
        Firmware = d.Firmware,
        IpAddress = d.IpAddress,
        Notes = d.Notes,
        GroupName = d.GroupName,
        ExpectedHeartbeatSeconds = d.ExpectedHeartbeatSeconds,
    };

    private static Device Map(DeviceEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Type = e.Type,
        Location = e.Location,
        Firmware = e.Firmware,
        IpAddress = e.IpAddress,
        Notes = e.Notes,
        GroupName = e.GroupName,
        ExpectedHeartbeatSeconds = e.ExpectedHeartbeatSeconds,
    };
}
