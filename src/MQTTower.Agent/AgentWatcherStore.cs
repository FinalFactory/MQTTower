using MQTTower.Core.Models;

namespace MQTTower.Agent;

/// <summary>Thread-safe copy of watcher definitions synced from the dashboard.</summary>
public sealed class AgentWatcherStore
{
    private readonly object _lock = new();
    private IReadOnlyList<TopicWatcher> _items = Array.Empty<TopicWatcher>();

    public void ReplaceAll(IReadOnlyList<TopicWatcher> items)
    {
        lock (_lock)
        {
            _items = items.ToList();
        }
    }

    public IReadOnlyList<TopicWatcher> Snapshot()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }
}
