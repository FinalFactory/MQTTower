using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Options;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using MQTTower.Infrastructure.Backup;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task Roundtrip_zip_contains_database()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mqttower_{Guid.NewGuid():N}.db");
        var opts = OptionsFactory.Create(new MqttTowerOptions { DatabasePath = $"Data Source={dbPath}" });
        var svc = new BackupService(opts);
        await File.WriteAllTextAsync(dbPath, "sqlite-placeholder");

        var zip = await svc.CreateBackupArchiveAsync();
        var ms = new MemoryStream(zip);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("mqttower.db").Should().NotBeNull();
    }
}
