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
            CapturedAt = snapshot.CapturedAt,
            Name = snapshot.Name,
            Value = snapshot.Value,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MetricSnapshot>> QueryAsync(string name, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var rows = await _db.MetricSnapshots.AsNoTracking()
            .Where(m => m.Name == name && m.CapturedAt >= from && m.CapturedAt <= to)
            .OrderBy(m => m.CapturedAt)
            .Select(m => new MetricSnapshot
            {
                Id = m.Id,
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
