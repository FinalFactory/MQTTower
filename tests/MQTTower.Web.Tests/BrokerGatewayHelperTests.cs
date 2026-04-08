using FluentAssertions;
using MQTTower.Core.Models;
using MQTTower.Web.Helpers;

namespace MQTTower.Web.Tests;

public sealed class BrokerGatewayHelperTests
{
    [Fact]
    public void CanUseGateway_true_when_online_approved_and_agent_url()
    {
        var b = new BrokerProfile
        {
            Id = Guid.NewGuid(),
            AgentUrl = "http://127.0.0.1:1",
            Approved = true,
            Status = BrokerStatus.Online,
        };

        BrokerGatewayHelper.CanUseGateway(b).Should().BeTrue();
        BrokerGatewayHelper.GetAgentUnavailableMessage(b).Should().BeNull();
    }

    [Fact]
    public void CanUseGateway_false_when_offline()
    {
        var b = new BrokerProfile
        {
            Id = Guid.NewGuid(),
            AgentUrl = "http://127.0.0.1:1",
            Approved = true,
            Status = BrokerStatus.Offline,
        };

        BrokerGatewayHelper.CanUseGateway(b).Should().BeFalse();
        BrokerGatewayHelper.GetAgentUnavailableMessage(b).Should().Contain("offline");
    }

    [Fact]
    public void CanUseGateway_false_when_not_approved()
    {
        var b = new BrokerProfile
        {
            Id = Guid.NewGuid(),
            AgentUrl = "http://127.0.0.1:1",
            Approved = false,
            Status = BrokerStatus.Pending,
        };

        BrokerGatewayHelper.CanUseGateway(b).Should().BeFalse();
        BrokerGatewayHelper.GetAgentUnavailableMessage(b).Should().Contain("not approved");
    }
}
