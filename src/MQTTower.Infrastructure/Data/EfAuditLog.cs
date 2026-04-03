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
        // SQLite stores Timestamp as TEXT; EF Core cannot translate DateTimeOffset range filters in LINQ.
        // Order, paging, and filters are in SQL (see EfMetricStore.QueryAsync, PruneOlderThanAsync).
        var hasUser = !string.IsNullOrWhiteSpace(userName);
        var f = from.HasValue;
        var t = to.HasValue;

        IQueryable<AuditEntryEntity> q = (f, t, hasUser) switch
        {
            (true, true, true) => _db.AuditEntries.FromSqlInterpolated(
                $"""
                SELECT Id, Timestamp, UserName, Action, EntityType, EntityName, Details
                FROM AuditEntries
                WHERE Timestamp >= {from!.Value} AND Timestamp <= {to!.Value} AND UserName = {userName!}
                ORDER BY Timestamp DESC
                LIMIT {take} OFFSET {skip}
                """),
            (true, true, false) => _db.AuditEntries.FromSqlInterpolated(
                $"""
                SELECT Id, Timestamp, UserName, Action, EntityType, EntityName, Details
                FROM AuditEntries
                WHERE Timestamp >= {from!.Value} AND Timestamp <= {to!.Value}
                ORDER BY Timestamp DESC
                LIMIT {take} OFFSET {skip}
                """),
            (true, false, true) => _db.AuditEntries.FromSqlInterpolated(
                $"""
                SELECT Id, Timestamp, UserName, Action, EntityType, EntityName, Details
                FROM AuditEntries
                WHERE Timestamp >= {from!.Value} AND UserName = {userName!}
                ORDER BY Timestamp DESC
                LIMIT {take} OFFSET {skip}
                """),
            (true, false, false) => _db.AuditEntries.FromSqlInterpolated(
                $"""
                SELECT Id, Timestamp, UserName, Action, EntityType, EntityName, Details
                FROM AuditEntries
                WHERE Timestamp >= {from!.Value}
                ORDER BY Timestamp DESC
                LIMIT {take} OFFSET {skip}
                """),
            (false, true, true) => _db.AuditEntries.FromSqlInterpolated(
                $"""
                SELECT Id, Timestamp, UserName, Action, EntityType, EntityName, Details
                FROM AuditEntries
                WHERE Timestamp <= {to!.Value} AND UserName = {userName!}
                ORDER BY Timestamp DESC
                LIMIT {take} OFFSET {skip}
                """),
            (false, true, false) => _db.AuditEntries.FromSqlInterpolated(
                $"""
                SELECT Id, Timestamp, UserName, Action, EntityType, EntityName, Details
                FROM AuditEntries
                WHERE Timestamp <= {to!.Value}
                ORDER BY Timestamp DESC
                LIMIT {take} OFFSET {skip}
                """),
            (false, false, true) => _db.AuditEntries.FromSqlInterpolated(
                $"""
                SELECT Id, Timestamp, UserName, Action, EntityType, EntityName, Details
                FROM AuditEntries
                WHERE UserName = {userName!}
                ORDER BY Timestamp DESC
                LIMIT {take} OFFSET {skip}
                """),
            (false, false, false) => _db.AuditEntries.FromSqlInterpolated(
                $"""
                SELECT Id, Timestamp, UserName, Action, EntityType, EntityName, Details
                FROM AuditEntries
                ORDER BY Timestamp DESC
                LIMIT {take} OFFSET {skip}
                """),
        };

        return await q
            .AsNoTracking()
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

    public async Task PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM AuditEntries WHERE Timestamp < {cutoff}",
            cancellationToken).ConfigureAwait(false);
    }
}
