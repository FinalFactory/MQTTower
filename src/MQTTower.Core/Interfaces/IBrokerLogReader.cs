namespace MQTTower.Core.Interfaces;

public interface IBrokerLogReader
{
    IAsyncEnumerable<string> TailAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns up to <paramref name="maxLines"/> lines from the end of the log file.</summary>
    Task<IReadOnlyList<string>> GetRecentLinesAsync(int maxLines, CancellationToken cancellationToken = default);
}
