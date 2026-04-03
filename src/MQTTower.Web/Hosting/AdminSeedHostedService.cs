using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Web.Hosting;

public sealed class AdminSeedHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;

    public AdminSeedHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.AppUsers.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var user = _configuration["MQTTower:AdminUser"] ?? Environment.GetEnvironmentVariable("MQTTOWER_ADMIN_USER") ?? "admin";
        var pass = _configuration["MQTTower:AdminPassword"] ?? Environment.GetEnvironmentVariable("MQTTOWER_ADMIN_PASS") ?? "changeme";
        db.AppUsers.Add(new AppUserEntity
        {
            Id = Guid.NewGuid(),
            UserName = user,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(pass),
            Role = AppUserRole.Admin,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Concurrent seed (parallel integration tests) or duplicate username.
            await using var scope2 = _scopeFactory.CreateAsyncScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db2.AppUsers.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
