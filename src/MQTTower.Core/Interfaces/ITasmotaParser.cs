using MQTTower.Core.Tasmota;

namespace MQTTower.Core.Interfaces;

public interface ITasmotaParser
{
    bool TryParse(string topic, string payloadJson, out TasmotaTelemetry telemetry);
}
