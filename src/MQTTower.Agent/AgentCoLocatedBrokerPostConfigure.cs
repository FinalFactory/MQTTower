using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Config;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Agent;

/// <summary>
/// The agent always runs beside Mosquitto: MQTT connects to localhost and the port is read from the same mosquitto.conf the dashboard edits.
/// </summary>
internal sealed class AgentCoLocatedBrokerPostConfigure : IPostConfigureOptions<MqttTowerOptions>
{
    public void PostConfigure(string? name, MqttTowerOptions options)
    {
        options.BrokerHost = "127.0.0.1";
        var path = options.MosquittoConfigPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var content = File.ReadAllText(path);
                options.BrokerPort = MosquittoConfigParser.ParseListenerPort(content);
            }
            catch
            {
                options.BrokerPort = 1883;
            }
        }
        else
        {
            options.BrokerPort = 1883;
        }
    }
}
