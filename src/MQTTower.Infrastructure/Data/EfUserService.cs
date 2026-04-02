using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Data;

public sealed class EfUserService : IUserService
{
    private readonly AppDbContext _db;

    public EfUserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AppUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var row = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(password, row.PasswordHash))
        {
            return null;
        }

        return Map(row);
    }

    public async Task CreateAsync(string userName, string password, AppUserRole role, CancellationToken cancellationToken = default)
    {
        _db.AppUsers.Add(new AppUserEntity
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _db.AppUsers.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.AppUsers.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var row = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        row.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AppUser Map(AppUserEntity e) => new()
    {
        Id = e.Id,
        UserName = e.UserName,
        PasswordHash = e.PasswordHash,
        Role = e.Role,
        CreatedAt = e.CreatedAt,
        LastLoginAt = e.LastLoginAt,
    };
}
