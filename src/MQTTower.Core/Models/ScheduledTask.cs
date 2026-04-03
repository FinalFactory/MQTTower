using System.ComponentModel.DataAnnotations;

namespace MQTTower.Core.Models;

public sealed class ScheduledTask
{
    public Guid Id { get; set; }
    public Guid? BrokerId { get; set; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string CronExpression { get; set; } = string.Empty;

    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public string Topic { get; set; } = string.Empty;

    [StringLength(65536)]
    public string Payload { get; set; } = string.Empty;
    public int Qos { get; set; }
    public bool Retain { get; set; }
    public bool Enabled { get; set; } = true;
}
