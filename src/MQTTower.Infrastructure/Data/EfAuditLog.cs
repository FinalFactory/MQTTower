using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Data;

public sealed class EfAuditLog : IAuditLog
{
    private readonly AppDbContext _db;

    public EfAuditLog(AppDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _db.AuditEntries.Add(new AuditEntryEntity
        {
            Timestamp = entry.Timestamp,
            UserName = entry.UserName,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityName = entry.EntityName,
            Details = entry.Details,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(DateTimeOffset? from, DateTimeOffset? to, string? userName, int skip, int take, CancellationToken cancellationToken = default)
    {
        var q = _db.AuditEntries.AsNoTracking().AsQueryable();
        if (from is not null)
        {
            q = q.Where(a => a.Timestamp >= from);
        }

        if (to is not null)
        {
            q = q.Where(a => a.Timestamp <= to);
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            q = q.Where(a => a.UserName == userName);
        }

        return await q.OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(take)
            .Select(a => new AuditEntry
            {
                Id = a.Id,
                Timestamp = a.Timestamp,
                UserName = a.UserName,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityName = a.EntityName,
                Details = a.Details,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
