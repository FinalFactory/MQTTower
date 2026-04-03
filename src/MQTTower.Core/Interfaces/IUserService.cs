using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IUserService
{
    Task<AppUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(string userName, string password, AppUserRole role, CancellationToken cancellationToken = default);
    /// <returns>True if the user existed and the password was updated.</returns>
    Task<bool> SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
