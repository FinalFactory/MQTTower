using System.Runtime.CompilerServices;
using MQTTower.Core.Interfaces;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Monitoring;

public sealed class BrokerLogReader : IBrokerLogReader
{
    private readonly MqttTowerOptions _options;

    public BrokerLogReader(IOptions<MqttTowerOptions> options)
    {
        _options = options.Value;
    }

    public async IAsyncEnumerable<string> TailAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_options.MosquittoLogPath))
        {
            yield break;
        }

        await using var stream = new FileStream(_options.MosquittoLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            yield return MosquittoLogParser.FormatLine(line);
        }
    }

    public async Task<IReadOnlyList<string>> GetRecentLinesAsync(int maxLines, CancellationToken cancellationToken = default)
    {
        if (maxLines <= 0 || !File.Exists(_options.MosquittoLogPath))
        {
            return Array.Empty<string>();
        }

        // Use shared read so Windows can read bind-mounted logs while Mosquitto has the file open (matches TailAsync).
        await using var stream = new FileStream(
            _options.MosquittoLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        using var textReader = new StreamReader(stream);
        var content = await textReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (content.Length == 0)
        {
            return Array.Empty<string>();
        }

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = MosquittoLogParser.FormatLine(lines[i]);
        }

        if (lines.Length <= maxLines)
        {
            return lines;
        }

        return lines[^maxLines..];
    }
}
