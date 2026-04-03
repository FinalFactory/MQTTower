namespace MQTTower.Core.Interfaces;

/// <summary>One-time agent registration tokens (minted by admin; consumed once at <c>POST /api/agents/register</c>).</summary>
public interface IRegistrationTokenService
{
    /// <summary>Mints a new token; returns the plaintext value once (store only a hash in the database).</summary>
    Task<string> MintAsync(DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegistrationTokenRow>> ListAsync(CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>If the plaintext matches an unused, non-expired token, marks it used and returns true.</summary>
    Task<bool> TryConsumeAsync(string plaintextToken, CancellationToken cancellationToken = default);
}

public sealed class RegistrationTokenRow
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? UsedAt { get; init; }
}
