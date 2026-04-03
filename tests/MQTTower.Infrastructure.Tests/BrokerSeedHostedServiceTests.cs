using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTower.Core;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Hosting;

namespace MQTTower.Infrastructure.Tests;

public sealed class BrokerSeedHostedServiceTests
{
    [Fact]
    public async Task StartAsync_seeds_default_local_broker_when_empty()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection), ServiceLifetime.Scoped);
        using var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var seed = new BrokerSeedHostedService(scopeFactory, NullLogger<BrokerSeedHostedService>.Instance);

        await seed.StartAsync(CancellationToken.None);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.BrokerProfiles.SingleAsync();
            row.Id.Should().Be(BrokerConstants.DefaultLocalBrokerId);
            row.UseLocalServices.Should().BeTrue();
        }

        await seed.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_is_idempotent_when_profiles_exist()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.BrokerProfiles.Add(new Data.Entities.BrokerProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "Existing",
                AgentUrl = "http://x",
                ApiKey = "k",
                Status = 0,
                RegisteredAt = DateTimeOffset.UtcNow,
                Approved = true,
                UseLocalServices = false,
            });
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection), ServiceLifetime.Scoped);
        using var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var seed = new BrokerSeedHostedService(scopeFactory, NullLogger<BrokerSeedHostedService>.Instance);

        await seed.StartAsync(CancellationToken.None);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.BrokerProfiles.CountAsync()).Should().Be(2);
            (await db.BrokerProfiles.AnyAsync(x => x.Id == BrokerConstants.DefaultLocalBrokerId)).Should().BeTrue();
        }
    }
}
