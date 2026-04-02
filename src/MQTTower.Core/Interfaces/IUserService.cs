using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IUserService
{
    Task<AppUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(string userName, string password, AppUserRole role, CancellationToken cancellationToken = default);
    Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
