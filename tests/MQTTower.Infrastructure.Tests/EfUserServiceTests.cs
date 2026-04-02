using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;

namespace MQTTower.Infrastructure.Tests;

public sealed class EfUserServiceTests
{
    [Fact]
    public async Task Create_authenticate_and_change_password()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"mqttower_test_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var svc = new EfUserService(db);

        await svc.CreateAsync("u1", "secret", AppUserRole.Admin);

        var u = await svc.AuthenticateAsync("u1", "secret");
        u.Should().NotBeNull();
        u!.UserName.Should().Be("u1");

        await svc.SetPasswordAsync(u.Id, "newsecret");
        (await svc.AuthenticateAsync("u1", "newsecret")).Should().NotBeNull();
    }
}
