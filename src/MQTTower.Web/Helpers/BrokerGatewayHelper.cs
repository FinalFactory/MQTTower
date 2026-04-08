using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Helpers;

public static class BrokerGatewayHelper
{
    /// <summary>
    /// When non-null, the UI should not call the agent and should show this message instead.
    /// </summary>
    public static string? GetAgentUnavailableMessage(BrokerProfile? broker)
    {
        if (broker is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(broker.AgentUrl))
        {
            return "No agent URL is configured for this broker.";
        }

        if (!broker.Approved)
        {
            return "This broker is not approved yet.";
        }

        if (broker.Status == BrokerStatus.Offline)
        {
            return "This broker is offline. The MQTT Tower agent could not be reached, so broker data is unavailable.";
        }

        if (broker.Status == BrokerStatus.Pending)
        {
            return "This broker is still pending approval.";
        }

        return null;
    }

    public static bool CanUseGateway(BrokerProfile? broker) =>
        GetAgentUnavailableMessage(broker) is null;

    public static async Task<T> WithGatewayAsync<T>(
        IBrokerGatewayFactory factory,
        BrokerProfile? broker,
        Func<IBrokerGateway, Task<T>> action,
        T defaultValue = default!)
    {
        if (broker is null || string.IsNullOrWhiteSpace(broker.AgentUrl))
        {
            return defaultValue;
        }

        if (!CanUseGateway(broker))
        {
            return defaultValue;
        }

        var gw = factory.Create(broker);
        try
        {
            return await action(gw).ConfigureAwait(false);
        }
        finally
        {
            (gw as IDisposable)?.Dispose();
        }
    }

    public static async Task WithGatewayAsync(
        IBrokerGatewayFactory factory,
        BrokerProfile? broker,
        Func<IBrokerGateway, Task> action)
    {
        if (broker is null || string.IsNullOrWhiteSpace(broker.AgentUrl))
        {
            return;
        }

        if (!CanUseGateway(broker))
        {
            return;
        }

        var gw = factory.Create(broker);
        try
        {
            await action(gw).ConfigureAwait(false);
        }
        finally
        {
            (gw as IDisposable)?.Dispose();
        }
    }
}
