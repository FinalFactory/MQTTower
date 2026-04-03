using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;

namespace MQTTower.Infrastructure.Tests;

public sealed class EfBrokerRegistryTests
{
    [Fact]
    public async Task Add_get_update_delete_roundtrip()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var reg = new EfBrokerRegistry(db);

        var id = Guid.NewGuid();
        var profile = new BrokerProfile
        {
            Id = id,
            Name = "B1",
            AgentUrl = "https://a:1/",
            ApiKey = "key",
            Status = BrokerStatus.Pending,
            RegisteredAt = DateTimeOffset.UtcNow,
            Approved = false,
            UseLocalServices = false,
        };

        await reg.AddAsync(profile);

        (await reg.GetByAgentUrlAsync("https://a:1/"))!.Id.Should().Be(id);

        var got = await reg.GetAsync(id);
        got.Should().NotBeNull();
        got!.Name.Should().Be("B1");
        got.AgentUrl.Should().Be("https://a:1/");

        profile.Name = "B1 renamed";
        profile.AgentUrl = "https://a:2/";
        profile.Notes = "note";
        await reg.UpdateAsync(profile);

        var after = await reg.GetAsync(id);
        after!.Name.Should().Be("B1 renamed");
        after.AgentUrl.Should().Be("https://a:2/");
        after.Notes.Should().Be("note");

        (await reg.ListAsync()).Should().HaveCount(1);

        await reg.DeleteAsync(id);
        (await reg.GetAsync(id)).Should().BeNull();
        (await reg.ListAsync()).Should().BeEmpty();
    }
}
