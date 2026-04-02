using System.Text.Json;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Tasmota;

namespace MQTTower.Infrastructure.Tasmota;

public sealed class TasmotaParser : ITasmotaParser
{
    public bool TryParse(string topic, string payloadJson, out TasmotaTelemetry telemetry)
    {
        telemetry = new TasmotaTelemetry();
        if (!topic.Contains("SENSOR", StringComparison.OrdinalIgnoreCase) && !topic.Contains("STATE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("ENERGY", out var energy))
            {
                if (energy.TryGetProperty("Power", out var p))
                {
                    telemetry.PowerWatts = p.GetDouble();
                }

                if (energy.TryGetProperty("Voltage", out var v))
                {
                    telemetry.Voltage = v.GetDouble();
                }

                if (energy.TryGetProperty("Current", out var c))
                {
                    telemetry.Current = c.GetDouble();
                }

                if (energy.TryGetProperty("Total", out var t))
                {
                    telemetry.EnergyKwh = t.GetDouble();
                }
            }

            if (root.TryGetProperty("AM2301", out var am))
            {
                if (am.TryGetProperty("Temperature", out var temp))
                {
                    telemetry.Temperature = temp.GetDouble();
                }

                if (am.TryGetProperty("Humidity", out var h))
                {
                    telemetry.Humidity = h.GetDouble();
                }
            }

            return telemetry.PowerWatts is not null || telemetry.Temperature is not null;
        }
        catch
        {
            return false;
        }
    }
}
