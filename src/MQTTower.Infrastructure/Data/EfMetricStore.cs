using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Data;

public sealed class EfMetricStore : IMetricStore
{
    private readonly AppDbContext _db;

    public EfMetricStore(AppDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(MetricSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _db.MetricSnapshots.Add(new MetricSnapshotEntity
        {
            BrokerId = snapshot.BrokerId,
            CapturedAt = snapshot.CapturedAt,
            Name = snapshot.Name,
            Value = snapshot.Value,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MetricSnapshot>> QueryAsync(string name, DateTimeOffset from, DateTimeOffset to, Guid? brokerId = null, CancellationToken cancellationToken = default)
    {
        // SQLite stores CapturedAt as TEXT; EF Core cannot translate DateTimeOffset range filters in LINQ.
        // Use parameterized SQL (same rationale as PruneOlderThanAsync).
        IQueryable<MetricSnapshotEntity> q = brokerId.HasValue
            ? _db.MetricSnapshots.FromSqlInterpolated(
                $"""
                SELECT Id, BrokerId, CapturedAt, Name, Value
                FROM MetricSnapshots
                WHERE Name = {name} AND CapturedAt >= {from} AND CapturedAt <= {to} AND BrokerId = {brokerId.Value}
                ORDER BY CapturedAt
                """)
            : _db.MetricSnapshots.FromSqlInterpolated(
                $"""
                SELECT Id, BrokerId, CapturedAt, Name, Value
                FROM MetricSnapshots
                WHERE Name = {name} AND CapturedAt >= {from} AND CapturedAt <= {to}
                ORDER BY CapturedAt
                """);

        var rows = await q
            .AsNoTracking()
            .Select(m => new MetricSnapshot
            {
                Id = m.Id,
                BrokerId = m.BrokerId,
                CapturedAt = m.CapturedAt,
                Name = m.Name,
                Value = m.Value,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    public async Task PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        // SQLite stores DateTimeOffset as TEXT; bulk LINQ deletes may not translate — use SQL.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM MetricSnapshots WHERE CapturedAt < {cutoff}",
            cancellationToken).ConfigureAwait(false);
    }
}
