using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Helpers;

public static class BrokerGatewayHelper
{
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
