using Microsoft.Extensions.Hosting;
using MQTTower.Infrastructure.Automation;

namespace MQTTower.Infrastructure.Hosting;

public sealed class WatcherStartupHostedService : IHostedService
{
    private readonly WatcherEngine _engine;

    public WatcherStartupHostedService(WatcherEngine engine)
    {
        _engine = engine;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _engine.Attach(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
