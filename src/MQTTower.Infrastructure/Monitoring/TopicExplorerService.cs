using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Mqtt;
using MQTTower.Core.TopicExplorer;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Monitoring;

public sealed class TopicExplorerService : ITopicExplorerService
{
    private readonly List<TopicTreeNode> _roots = new();
    private readonly object _gate = new();
    private readonly int _maxNodes;
    private readonly ILogger<TopicExplorerService> _logger;
    private int _nodeCount;

    public TopicExplorerService(IOptions<MqttTowerOptions> options, ILogger<TopicExplorerService> logger)
    {
        var n = options.Value.TopicExplorerMaxNodes;
        _maxNodes = n > 0 ? n : 10_000;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public void Attach(IMqttSubscriber subscriber, CancellationToken cancellationToken)
    {
        _ = subscriber.SubscribeAsync("#", OnMessageAsync, cancellationToken).ContinueWith(
            t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception!.GetBaseException(), "Topic explorer MQTT subscribe failed");
                }
            },
            cancellationToken,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
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
                if (_nodeCount >= _maxNodes)
                {
                    _logger.LogWarning("Topic explorer node cap ({Max}) reached; ignoring new topic segments", _maxNodes);
                    return;
                }

                node = new TopicTreeNode
                {
                    Segment = segments[i],
                    FullTopic = path,
                };
                level.Add(node);
                _nodeCount++;
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
