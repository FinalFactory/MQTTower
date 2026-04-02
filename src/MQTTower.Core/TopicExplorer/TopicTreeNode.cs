namespace MQTTower.Core.TopicExplorer;

public sealed class TopicTreeNode
{
    public string Segment { get; set; } = string.Empty;
    public string FullTopic { get; set; } = string.Empty;
    public string? LastPayloadPreview { get; set; }
    public int MessageCount { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public List<TopicTreeNode> Children { get; } = new();
}
