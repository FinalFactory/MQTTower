using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IBrokerGatewayFactory
{
    IBrokerGateway Create(BrokerProfile broker);
}
