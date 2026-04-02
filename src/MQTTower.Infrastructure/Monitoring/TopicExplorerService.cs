using MQTTower.Core.Interfaces;
using MQTTower.Core.Mqtt;
using MQTTower.Core.TopicExplorer;

namespace MQTTower.Infrastructure.Monitoring;

public sealed class TopicExplorerService : ITopicExplorerService
{
    private readonly List<TopicTreeNode> _roots = new();
    private readonly object _gate = new();

    public event EventHandler? Changed;

    public void Attach(IMqttSubscriber subscriber, CancellationToken cancellationToken)
    {
        _ = subscriber.SubscribeAsync("#", OnMessageAsync, cancellationToken);
    }

    private Task OnMessageAsync(MqttAppMessage msg)
    {
        var preview = System.Text.Encoding.UTF8.GetString(msg.Payload);
        if (preview.Length > 256)
        {
            preview = preview[..256];
        }

        lock (_gate)
        {
            Insert(msg.Topic, preview);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void Insert(string topic, string preview)
    {
        var segments = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return;
        }

        var level = _roots;
        var path = string.Empty;
        TopicTreeNode? node = null;
        for (var i = 0; i < segments.Length; i++)
        {
            path = i == 0 ? segments[i] : path + "/" + segments[i];
            node = level.FirstOrDefault(n => n.Segment == segments[i]);
            if (node is null)
            {
                node = new TopicTreeNode
                {
                    Segment = segments[i],
                    FullTopic = path,
                };
                level.Add(node);
            }

            if (i == segments.Length - 1)
            {
                node.LastPayloadPreview = preview;
                node.MessageCount++;
                node.LastUpdated = DateTimeOffset.UtcNow;
            }

            level = node.Children;
        }
    }

    public IReadOnlyList<TopicTreeNode> GetRoots()
    {
        lock (_gate)
        {
            return _roots.ToList();
        }
    }
}
