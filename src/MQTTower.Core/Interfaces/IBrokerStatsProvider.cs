using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IBrokerStatsProvider
{
    BrokerStats GetCurrent();
}
