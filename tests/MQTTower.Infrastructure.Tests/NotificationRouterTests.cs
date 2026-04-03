using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Data.Entities;
using MQTTower.Infrastructure.Notifications;
using NSubstitute;

namespace MQTTower.Infrastructure.Tests;

public sealed class NotificationRouterTests
{
    [Fact]
    public async Task Dispatches_to_matching_channel()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"mqttower_nr_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.NotificationRules.Add(new NotificationRuleEntity
        {
            Id = Guid.NewGuid(),
            Name = "t",
            TriggerType = "watcher",
            ConfigJson = "{}",
            Channel = "ntfy",
            Enabled = true,
        });
        await db.SaveChangesAsync();

        var ch = Substitute.For<MQTTower.Core.Interfaces.INotificationChannel>();
        ch.ChannelId.Returns("ntfy");

        var router = new NotificationRouter(db, new[] { ch }, NullLogger<NotificationRouter>.Instance);

        await router.DispatchAsync("watcher", "{}", CancellationToken.None);

        await ch.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
