using MQTTower.Core.Models;
using MQTTower.Core.TopicExplorer;

namespace MQTTower.Core.Interfaces;

/// <summary>Per-broker operations via the agent HTTP API.</summary>
public interface IBrokerGateway
{
    Guid BrokerId { get; }

    Task<BrokerStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qos, bool retain, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MqttClientInfo>> ListClientsAsync(CancellationToken cancellationToken = default);
    Task<MqttClientInfo> GetClientAsync(string username, CancellationToken cancellationToken = default);
    Task CreateClientAsync(string username, string password, IReadOnlyList<string>? roles, IReadOnlyList<string>? groups, CancellationToken cancellationToken = default);
    Task DeleteClientAsync(string username, CancellationToken cancellationToken = default);
    Task SetClientEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default);
    Task SetClientPasswordAsync(string username, string password, CancellationToken cancellationToken = default);
    Task AddClientRoleAsync(string username, string rolename, int priority = -1, CancellationToken cancellationToken = default);
    Task RemoveClientRoleAsync(string username, string rolename, CancellationToken cancellationToken = default);
    Task AddGroupClientAsync(string groupname, string username, int priority = -1, CancellationToken cancellationToken = default);
    Task RemoveGroupClientAsync(string groupname, string username, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MqttRole>> ListRolesAsync(CancellationToken cancellationToken = default);
    Task<MqttRole> GetRoleAsync(string name, CancellationToken cancellationToken = default);
    Task CreateRoleAsync(string name, string? description, IReadOnlyList<AclEntry> acls, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(string name, CancellationToken cancellationToken = default);
    Task AddRoleAclAsync(string rolename, AclEntry acl, CancellationToken cancellationToken = default);
    Task RemoveRoleAclAsync(string rolename, AclEntry acl, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MqttGroup>> ListGroupsAsync(CancellationToken cancellationToken = default);
    Task<MqttGroup> GetGroupAsync(string name, CancellationToken cancellationToken = default);
    Task CreateGroupAsync(string name, string? description, IReadOnlyList<string> roleNames, IReadOnlyList<string> clientUsernames, CancellationToken cancellationToken = default);
    Task DeleteGroupAsync(string name, CancellationToken cancellationToken = default);
    Task AddGroupRoleAsync(string groupname, string rolename, int priority = -1, CancellationToken cancellationToken = default);
    Task RemoveGroupRoleAsync(string groupname, string rolename, CancellationToken cancellationToken = default);

    Task<string> ReadConfigAsync(CancellationToken cancellationToken = default);
    Task WriteConfigAsync(string content, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRecentLogsAsync(int maxLines, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopicTreeNode>> GetTopicRootsAsync(CancellationToken cancellationToken = default);

    Task<AgentInfo> GetHealthAsync(CancellationToken cancellationToken = default);

    Task RestartBrokerAsync(CancellationToken cancellationToken = default);
}
