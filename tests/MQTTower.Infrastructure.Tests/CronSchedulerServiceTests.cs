using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTower.Infrastructure.Automation;
using MQTTower.Infrastructure.Data;

namespace MQTTower.Infrastructure.Tests;

public sealed class CronSchedulerServiceTests
{
    [Fact]
    public async Task ListAsync_returns_empty_when_no_tasks()
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
        var scheduler = new CronSchedulerService(scopeFactory, NullLogger<CronSchedulerService>.Instance);

        var list = await scheduler.ListAsync();

        list.Should().BeEmpty();
    }
}
