namespace MQTTower.Infrastructure.Data.Entities;

public sealed class RegistrationTokenEntity
{
    public Guid Id { get; set; }

    /// <summary>SHA256 hex of UTF-8 plaintext token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
