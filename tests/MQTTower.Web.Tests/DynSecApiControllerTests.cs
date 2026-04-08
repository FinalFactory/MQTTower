using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Web.Controllers;
using NSubstitute;

namespace MQTTower.Web.Tests;

public sealed class DynSecApiControllerTests
{
    [Fact]
    public async Task Clients_with_brokerId_resolves_profile_and_lists_clients_on_gateway()
    {
        var brokerId = Guid.NewGuid();
        var profile = new BrokerProfile
        {
            Id = brokerId,
            Name = "remote",
            AgentUrl = "http://127.0.0.1:1",
            Approved = true,
            Status = BrokerStatus.Online,
        };

        var registry = Substitute.For<IBrokerRegistry>();
        registry.GetAsync(brokerId, Arg.Any<CancellationToken>()).Returns(profile);

        var gateway = Substitute.For<IBrokerGateway>();
        gateway.ListClientsAsync(Arg.Any<CancellationToken>()).Returns(new List<MqttClientInfo>());

        var factory = Substitute.For<IBrokerGatewayFactory>();
        factory.Create(profile).Returns(gateway);

        var controller = new DynSecApiController(registry, factory);
        var result = await controller.Clients(brokerId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await registry.Received(1).GetAsync(brokerId, Arg.Any<CancellationToken>());
        factory.Received(1).Create(profile);
    }

    [Fact]
    public async Task Clients_without_brokerId_uses_default_local_when_present()
    {
        var profile = new BrokerProfile
        {
            Id = Guid.NewGuid(),
            Name = "local",
            AgentUrl = "http://127.0.0.1:1",
            Approved = true,
            Status = BrokerStatus.Online,
        };
        var registry = Substitute.For<IBrokerRegistry>();
        registry.GetDefaultLocalAsync(Arg.Any<CancellationToken>()).Returns(profile);

        var gateway = Substitute.For<IBrokerGateway>();
        gateway.ListClientsAsync(Arg.Any<CancellationToken>()).Returns(new List<MqttClientInfo>());

        var factory = Substitute.For<IBrokerGatewayFactory>();
        factory.Create(profile).Returns(gateway);

        var controller = new DynSecApiController(registry, factory);
        var result = await controller.Clients(null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await registry.Received(1).GetDefaultLocalAsync(Arg.Any<CancellationToken>());
        factory.Received(1).Create(profile);
    }

    [Fact]
    public async Task Roles_with_brokerId_resolves_profile_via_GetAsync()
    {
        var brokerId = Guid.NewGuid();
        var profile = new BrokerProfile
        {
            Id = brokerId,
            Name = "r",
            AgentUrl = "http://127.0.0.1:1",
            Approved = true,
            Status = BrokerStatus.Online,
        };

        var registry = Substitute.For<IBrokerRegistry>();
        registry.GetAsync(brokerId, Arg.Any<CancellationToken>()).Returns(profile);

        var gateway = Substitute.For<IBrokerGateway>();
        gateway.ListRolesAsync(Arg.Any<CancellationToken>()).Returns(new List<MqttRole>());

        var factory = Substitute.For<IBrokerGatewayFactory>();
        factory.Create(profile).Returns(gateway);

        var controller = new DynSecApiController(registry, factory);
        var result = await controller.Roles(brokerId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await registry.Received(1).GetAsync(brokerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clients_returns_503_when_broker_is_offline()
    {
        var brokerId = Guid.NewGuid();
        var profile = new BrokerProfile
        {
            Id = brokerId,
            Name = "down",
            AgentUrl = "http://127.0.0.1:1",
            Approved = true,
            Status = BrokerStatus.Offline,
        };

        var registry = Substitute.For<IBrokerRegistry>();
        registry.GetAsync(brokerId, Arg.Any<CancellationToken>()).Returns(profile);

        var factory = Substitute.For<IBrokerGatewayFactory>();
        var controller = new DynSecApiController(registry, factory);
        var result = await controller.Clients(brokerId, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)result!).StatusCode.Should().Be(503);
        factory.DidNotReceive().Create(Arg.Any<BrokerProfile>());
    }
}
