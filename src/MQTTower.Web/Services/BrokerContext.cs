using MQTTower.Core.Models;

namespace MQTTower.Web.Services;

public sealed class BrokerContext
{
    public BrokerProfile? Selected { get; private set; }

    public event Action? Changed;

    public void Select(BrokerProfile? broker)
    {
        if (MatchesSnapshot(Selected, broker))
        {
            return;
        }

        Selected = broker;
        Changed?.Invoke();
    }

    private static bool MatchesSnapshot(BrokerProfile? a, BrokerProfile? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.Id == b.Id
            && a.Status == b.Status
            && a.Name == b.Name
            && a.AgentUrl == b.AgentUrl
            && a.Approved == b.Approved
            && a.LastSeen == b.LastSeen;
    }
}
