using Microsoft.EntityFrameworkCore;
using MQTTower.Core;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Infrastructure.Data;

public sealed class EfBrokerRegistry : IBrokerRegistry
{
    private readonly AppDbContext _db;

    public EfBrokerRegistry(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<BrokerProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.BrokerProfiles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task<BrokerProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _db.BrokerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<BrokerProfile?> GetByAgentUrlAsync(string agentUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentUrl))
        {
            return null;
        }

        var normalized = agentUrl.Trim();
        var row = await _db.BrokerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.AgentUrl == normalized, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<BrokerProfile?> GetDefaultLocalAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.BrokerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UseLocalServices, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<BrokerProfile> AddAsync(BrokerProfile profile, CancellationToken cancellationToken = default)
    {
        var e = Map(profile);
        _db.BrokerProfiles.Add(e);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(e);
    }

    public async Task UpdateAsync(BrokerProfile profile, CancellationToken cancellationToken = default)
    {
        var e = await _db.BrokerProfiles.FirstOrDefaultAsync(x => x.Id == profile.Id, cancellationToken).ConfigureAwait(false);
        if (e is null)
        {
            throw new InvalidOperationException("Broker not found");
        }

        e.Name = profile.Name;
        e.AgentUrl = profile.AgentUrl;
        e.ApiKey = profile.ApiKey;
        e.TlsCertThumbprint = profile.TlsCertThumbprint;
        e.Status = (int)profile.Status;
        e.LastSeen = profile.LastSeen;
        e.RegisteredAt = profile.RegisteredAt;
        e.Approved = profile.Approved;
        e.Notes = profile.Notes;
        e.UseLocalServices = profile.UseLocalServices;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == BrokerConstants.DefaultLocalBrokerId)
        {
            throw new InvalidOperationException("The default local broker profile cannot be deleted.");
        }

        await _db.BrokerProfiles.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BrokerProfile Map(Entities.BrokerProfileEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        AgentUrl = e.AgentUrl,
        ApiKey = e.ApiKey,
        TlsCertThumbprint = e.TlsCertThumbprint,
        Status = (BrokerStatus)e.Status,
        LastSeen = e.LastSeen,
        RegisteredAt = e.RegisteredAt,
        Approved = e.Approved,
        Notes = e.Notes,
        UseLocalServices = e.UseLocalServices,
    };

    private static Entities.BrokerProfileEntity Map(BrokerProfile p) => new()
    {
        Id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id,
        Name = p.Name,
        AgentUrl = p.AgentUrl,
        ApiKey = p.ApiKey,
        TlsCertThumbprint = p.TlsCertThumbprint,
        Status = (int)p.Status,
        LastSeen = p.LastSeen,
        RegisteredAt = p.RegisteredAt,
        Approved = p.Approved,
        Notes = p.Notes,
        UseLocalServices = p.UseLocalServices,
    };
}
