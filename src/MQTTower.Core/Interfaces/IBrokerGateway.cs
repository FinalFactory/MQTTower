using MQTTower.Core.Models;
using MQTTower.Core.TopicExplorer;

namespace MQTTower.Core.Interfaces;

/// <summary>Per-broker operations (local in-process or remote agent HTTP).</summary>
public interface IBrokerGateway
{
    Guid BrokerId { get; }

    Task<BrokerStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qos, bool retain, CancellationToken cancellationToken = default);

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

    Task<string> ReadConfigAsync(CancellationToken cancellationToken = default);
    Task WriteConfigAsync(string content, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRecentLogsAsync(int maxLines, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopicTreeNode>> GetTopicRootsAsync(CancellationToken cancellationToken = default);

    Task<AgentInfo> GetHealthAsync(CancellationToken cancellationToken = default);

    Task RestartBrokerAsync(CancellationToken cancellationToken = default);
}
