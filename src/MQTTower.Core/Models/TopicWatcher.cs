namespace MQTTower.Core.Models;

public sealed class TopicWatcher
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TopicPattern { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string ActionType { get; set; } = "Notification";
    public string ActionConfigJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
}
