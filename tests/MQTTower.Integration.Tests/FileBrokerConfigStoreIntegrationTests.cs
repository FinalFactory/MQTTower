using FluentAssertions;
using Microsoft.Extensions.Options;
using MQTTower.Integration.Tests.Fixtures;
using MQTTower.Infrastructure.Config;

namespace MQTTower.Integration.Tests;

[Trait("Category", "Integration")]
[Collection("Mosquitto")]
public sealed class FileBrokerConfigStoreIntegrationTests(MosquittoFixture fixture)
{
    [Fact]
    public async Task ReadAsync_read_host_mosquitto_conf()
    {
        var store = new FileBrokerConfigStore(Options.Create(fixture.CreateOptions()));
        var text = await store.ReadAsync();
        text.Should().Contain("listener");
        text.Should().Contain("dynamic-security");
    }

    [Fact]
    public async Task WriteAsync_creates_backup_and_replaces_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mqttower-it-cfg-{Guid.NewGuid():N}.conf");
        try
        {
            await File.WriteAllTextAsync(path, "orig=1\n");
            var o = fixture.CreateOptions(x => x.MosquittoConfigPath = path);
            var store = new FileBrokerConfigStore(Options.Create(o));
            await store.WriteAsync("new=2\n");
            (await store.ReadAsync()).Should().Be("new=2\n");
            File.Exists(path + ".bak").Should().BeTrue();
            (await File.ReadAllTextAsync(path + ".bak")).Should().Be("orig=1\n");
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + ".bak");
        }
    }

    private static void TryDelete(string p)
    {
        try
        {
            if (File.Exists(p))
            {
                File.Delete(p);
            }
        }
        catch
        {
            /* ignore */
        }
    }
}
