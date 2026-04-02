namespace MQTTower.Core.Interfaces;

public interface IBrokerConfigStore
{
    Task<string> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(string content, CancellationToken cancellationToken = default);
}
