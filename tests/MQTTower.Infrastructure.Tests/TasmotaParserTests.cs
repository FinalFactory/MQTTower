using FluentAssertions;
using MQTTower.Infrastructure.Tasmota;

namespace MQTTower.Infrastructure.Tests;

public sealed class TasmotaParserTests
{
    [Fact]
    public void Parses_sensor_sample()
    {
        var p = new TasmotaParser();
        const string json = """{"ENERGY":{"Power":120.5,"Voltage":230.1},"AM2301":{"Temperature":21.2,"Humidity":48}}""";
        var ok = p.TryParse("tele/tasmota/SENSOR", json, out var t);
        ok.Should().BeTrue();
        t.PowerWatts.Should().Be(120.5);
        t.Temperature.Should().Be(21.2);
    }
}
