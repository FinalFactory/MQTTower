using System.ComponentModel.DataAnnotations;

namespace MQTTower.Core.Models;

public sealed class TopicWatcher
{
    public Guid Id { get; set; }
    public Guid? BrokerId { get; set; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string TopicPattern { get; set; } = string.Empty;

    [StringLength(2048)]
    public string Condition { get; set; } = string.Empty;

    [StringLength(64)]
    public string ActionType { get; set; } = "Notification";

    [StringLength(8192)]
    public string ActionConfigJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
}
