namespace MQTTower.Core.Models;

public sealed class BrokerStats
{
    public int ConnectedClients { get; set; }
    public double MessagesPerSecond { get; set; }
    public int ActiveTopics { get; set; }
    public double DataRateBytesPerSecond { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ConnectedClientsDeltaThisHour { get; set; }
    public double MessagesPerSecondDeltaPercent { get; set; }
}
