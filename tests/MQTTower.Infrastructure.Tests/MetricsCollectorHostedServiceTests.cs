using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MQTTower.Core;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Hosting;
using MQTTower.Infrastructure.Options;
using NSubstitute;

namespace MQTTower.Infrastructure.Tests;

public sealed class MetricsCollectorHostedServiceTests
{
    [Fact]
    public async Task CollectMetricsOnceAsync_appends_two_snapshots_and_prunes()
    {
        var metrics = new RecordingMetricStore();
        var registry = Substitute.For<IBrokerRegistry>();
        registry.GetDefaultLocalAsync(Arg.Any<CancellationToken>())
            .Returns(new BrokerProfile
            {
                Id = BrokerConstants.DefaultLocalBrokerId,
                Name = "Local",
                UseLocalServices = true,
                RegisteredAt = DateTimeOffset.UtcNow,
                Approved = true,
            });

        var services = new ServiceCollection();
        services.AddSingleton(metrics);
        services.AddScoped<IMetricStore>(_ => metrics);
        services.AddScoped<IBrokerRegistry>(_ => registry);
        services.AddScoped<IAuditLog>(_ => Substitute.For<IAuditLog>());
        using var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var stats = Substitute.For<IBrokerStatsProvider>();
        stats.GetCurrent().Returns(new BrokerStats { MessagesPerSecond = 3.5, ConnectedClients = 7 });

        var options = new MqttTowerOptions { MetricsRetentionDays = 30 };
        await MetricsCollectorHostedService.CollectMetricsOnceAsync(scopeFactory, stats, options, CancellationToken.None);

        metrics.Appended.Should().HaveCount(2);
        metrics.Appended[0].Name.Should().Be("messagesPerSecond");
        metrics.Appended[0].Value.Should().Be(3.5);
        metrics.Appended[1].Name.Should().Be("connectedClients");
        metrics.Appended[1].Value.Should().Be(7);
        metrics.PruneCalls.Should().Be(1);
    }

    private sealed class RecordingMetricStore : IMetricStore
    {
        public List<MetricSnapshot> Appended { get; } = new();
        public int PruneCalls { get; private set; }

        public Task AppendAsync(MetricSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Appended.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MetricSnapshot>> QueryAsync(string name, DateTimeOffset from, DateTimeOffset to, Guid? brokerId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MetricSnapshot>>(Array.Empty<MetricSnapshot>());

        public Task PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            PruneCalls++;
            return Task.CompletedTask;
        }
    }
}
