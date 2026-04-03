using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Interfaces;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Data;

public sealed class EfRegistrationTokenService : IRegistrationTokenService
{
    private readonly AppDbContext _db;

    public EfRegistrationTokenService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> MintAsync(DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default)
    {
        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Expiry must be in the future.");
        }

        var plaintext = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var hash = Hash(plaintext);
        _db.RegistrationTokens.Add(new RegistrationTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAtUtc,
            UsedAt = null,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return plaintext;
    }

    public async Task<IReadOnlyList<RegistrationTokenRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.RegistrationTokens.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(t => new RegistrationTokenRow
        {
            Id = t.Id,
            CreatedAt = t.CreatedAt,
            ExpiresAt = t.ExpiresAt,
            UsedAt = t.UsedAt,
        }).ToList();
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _db.RegistrationTokens.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryConsumeAsync(string plaintextToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken))
        {
            return false;
        }

        var tokenHash = Hash(plaintextToken.Trim());
        var now = DateTimeOffset.UtcNow;

        // Single atomic UPDATE (raw SQL: ExecuteUpdate cannot translate nullable DateTimeOffset filters on SQLite).
        return await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE RegistrationTokens
            SET UsedAt = {now}
            WHERE TokenHash = {tokenHash}
              AND UsedAt IS NULL
              AND (ExpiresAt IS NULL OR {now} <= ExpiresAt)
            """,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    private static string Hash(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
