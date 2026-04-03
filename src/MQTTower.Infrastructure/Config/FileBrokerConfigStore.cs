using MQTTower.Core.Interfaces;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Config;

public sealed class FileBrokerConfigStore : IBrokerConfigStore
{
    private readonly MqttTowerOptions _options;

    public FileBrokerConfigStore(IOptions<MqttTowerOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_options.MosquittoConfigPath))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(_options.MosquittoConfigPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(string content, CancellationToken cancellationToken = default)
    {
        var path = _options.MosquittoConfigPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }

        var dirName = Path.GetDirectoryName(path);
        var tmp = Path.Combine(string.IsNullOrEmpty(dirName) ? "." : dirName, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tmp, content, cancellationToken).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try
                {
                    File.Delete(tmp);
                }
                catch
                {
                    // best-effort cleanup of temp file
                }
            }
        }
    }
}
