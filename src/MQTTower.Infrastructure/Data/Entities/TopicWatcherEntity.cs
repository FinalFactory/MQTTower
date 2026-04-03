namespace MQTTower.Infrastructure.Data.Entities;

public sealed class TopicWatcherEntity
{
    public Guid Id { get; set; }
    public Guid? BrokerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TopicPattern { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string ActionType { get; set; } = "Notification";
    public string ActionConfigJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
}
