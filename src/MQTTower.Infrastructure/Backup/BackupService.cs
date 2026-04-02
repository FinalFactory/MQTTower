using System.IO.Compression;
using System.Text;
using MQTTower.Core.Interfaces;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Backup;

public sealed class BackupService : IBackupService
{
    private readonly MqttTowerOptions _options;

    public BackupService(IOptions<MqttTowerOptions> options)
    {
        _options = options.Value;
    }

    public async Task<byte[]> CreateBackupArchiveAsync(CancellationToken cancellationToken = default)
    {
        await using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var dbBytes = await File.ReadAllBytesAsync(SqlitePath(), cancellationToken).ConfigureAwait(false);
            var dbEntry = zip.CreateEntry("mqttower.db");
            await using (var s = dbEntry.Open())
            {
                await s.WriteAsync(dbBytes, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_options.MosquittoConfigPath))
            {
                var cfg = await File.ReadAllTextAsync(_options.MosquittoConfigPath, cancellationToken).ConfigureAwait(false);
                var cfgEntry = zip.CreateEntry("mosquitto.conf");
                await using var s = cfgEntry.Open();
                await s.WriteAsync(Encoding.UTF8.GetBytes(cfg), cancellationToken).ConfigureAwait(false);
            }
        }

        return ms.ToArray();
    }

    public async Task RestoreFromArchiveAsync(Stream zipStream, CancellationToken cancellationToken = default)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var db = zip.GetEntry("mqttower.db");
        if (db is not null)
        {
            var path = SqlitePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var source = db.Open();
            await using var dest = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }
    }

    private string SqlitePath()
    {
        var cs = _options.DatabasePath;
        const string prefix = "Data Source=";
        return cs.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? cs[prefix.Length..].Trim()
            : "mqttower.db";
    }
}
