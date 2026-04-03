using System.ComponentModel.DataAnnotations;

namespace MQTTower.Core.Models;

public sealed class NotificationRule
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string TriggerType { get; set; } = string.Empty;

    [StringLength(8192)]
    public string ConfigJson { get; set; } = "{}";

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Channel { get; set; } = "ntfy";
    public bool Enabled { get; set; } = true;
}
