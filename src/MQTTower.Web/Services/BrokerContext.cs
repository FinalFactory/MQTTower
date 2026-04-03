using MQTTower.Core.Models;

namespace MQTTower.Web.Services;

public sealed class BrokerContext
{
    public BrokerProfile? Selected { get; private set; }

    public event Action? Changed;

    public void Select(BrokerProfile? broker)
    {
        Selected = broker;
        Changed?.Invoke();
    }
}
