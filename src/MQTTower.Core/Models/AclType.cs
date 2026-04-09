namespace MQTTower.Core.Models;

public enum AclType
{
    Publish = 0,
    Subscribe = 1,
    PublishSubscribe = 2,
    /// <summary>Mosquitto <c>publishClientReceive</c>.</summary>
    PublishReceive = 3,
    /// <summary>Mosquitto <c>unsubscribePattern</c>.</summary>
    Unsubscribe = 4,
}
