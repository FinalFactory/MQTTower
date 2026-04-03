using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Web.Controllers;
using NSubstitute;

namespace MQTTower.Web.Tests;

public sealed class ApiControllersTests
{
    [Fact]
    public async Task DevicesApiController_List_delegates_to_registry()
    {
        var reg = Substitute.For<IDeviceRegistry>();
        reg.ListAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new List<Device>());
        var c = new DevicesApiController(reg);

        await c.List(null, CancellationToken.None);

        await reg.Received(1).ListAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WatchersApiController_List_delegates_to_service()
    {
        var svc = Substitute.For<IWatcherService>();
        svc.ListAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new List<TopicWatcher>());
        var c = new WatchersApiController(svc);

        await c.List(null, CancellationToken.None);

        await svc.Received(1).ListAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SchedulerApiController_List_delegates_to_service()
    {
        var svc = Substitute.For<ISchedulerService>();
        svc.ListAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new List<ScheduledTask>());
        var c = new SchedulerApiController(svc);

        await c.List(null, CancellationToken.None);

        await svc.Received(1).ListAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotificationRulesApiController_List_delegates_to_router()
    {
        var router = Substitute.For<INotificationRouter>();
        router.ListRulesAsync(Arg.Any<CancellationToken>()).Returns(new List<NotificationRule>());
        var c = new NotificationRulesApiController(router);

        await c.List(CancellationToken.None);

        await router.Received(1).ListRulesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class ApiModelValidationTests
{
    [Fact]
    public void Device_requires_name()
    {
        var d = new Device { Name = "" };
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(d, new ValidationContext(d), results, true);
        ok.Should().BeFalse();
    }

    [Fact]
    public void TopicWatcher_requires_name_and_pattern()
    {
        var w = new TopicWatcher { Name = "", TopicPattern = "" };
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(w, new ValidationContext(w), results, true);
        ok.Should().BeFalse();
    }
}
