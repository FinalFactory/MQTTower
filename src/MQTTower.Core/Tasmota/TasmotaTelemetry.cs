namespace MQTTower.Core.Tasmota;

public sealed class TasmotaTelemetry
{
    public double? PowerWatts { get; set; }
    public double? Voltage { get; set; }
    public double? Current { get; set; }
    public double? EnergyKwh { get; set; }
    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
}
