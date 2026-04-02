namespace MQTTower.Core.Interfaces;

public interface INotificationChannel
{
    string ChannelId { get; }
    Task SendAsync(string title, string body, CancellationToken cancellationToken = default);
}
