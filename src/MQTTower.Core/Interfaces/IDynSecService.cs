using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IDynSecService
{
    Task<IReadOnlyList<MqttClientInfo>> ListClientsAsync(CancellationToken cancellationToken = default);
    Task CreateClientAsync(string username, string password, IReadOnlyList<string>? roles, IReadOnlyList<string>? groups, CancellationToken cancellationToken = default);
    Task DeleteClientAsync(string username, CancellationToken cancellationToken = default);
    Task SetClientEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MqttRole>> ListRolesAsync(CancellationToken cancellationToken = default);
    Task CreateRoleAsync(string name, string? description, IReadOnlyList<AclEntry> acls, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MqttGroup>> ListGroupsAsync(CancellationToken cancellationToken = default);
    Task CreateGroupAsync(string name, string? description, IReadOnlyList<string> roleNames, IReadOnlyList<string> clientUsernames, CancellationToken cancellationToken = default);
    Task DeleteGroupAsync(string name, CancellationToken cancellationToken = default);
}
