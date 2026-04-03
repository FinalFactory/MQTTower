using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Core.TopicExplorer;
using MQTTower.Infrastructure.Mqtt;

namespace MQTTower.Infrastructure.Agents;

public sealed class LocalBrokerGateway : IBrokerGateway
{
    private readonly Guid _brokerId;
    private readonly IDynSecService _dynSec;
    private readonly IBrokerStatsProvider _stats;
    private readonly IBrokerConfigStore _config;
    private readonly IBrokerLogReader _logs;
    private readonly ITopicExplorerService _topics;
    private readonly IMqttPublisher _publisher;
    private readonly MqttConnectionService _mqtt;

    public LocalBrokerGateway(
        Guid brokerId,
        IDynSecService dynSec,
        IBrokerStatsProvider stats,
        IBrokerConfigStore config,
        IBrokerLogReader logs,
        ITopicExplorerService topics,
        IMqttPublisher publisher,
        MqttConnectionService mqtt)
    {
        _brokerId = brokerId;
        _dynSec = dynSec;
        _stats = stats;
        _config = config;
        _logs = logs;
        _topics = topics;
        _publisher = publisher;
        _mqtt = mqtt;
    }

    public Guid BrokerId => _brokerId;

    public Task<BrokerStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_stats.GetCurrent());

    public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qos, bool retain, CancellationToken cancellationToken = default) =>
        _publisher.PublishAsync(topic, payload, qos, retain, cancellationToken);

    public Task<IReadOnlyList<MqttClientInfo>> ListClientsAsync(CancellationToken cancellationToken = default) =>
        _dynSec.ListClientsAsync(cancellationToken);

    public Task CreateClientAsync(string username, string password, IReadOnlyList<string>? roles, IReadOnlyList<string>? groups, CancellationToken cancellationToken = default) =>
        _dynSec.CreateClientAsync(username, password, roles, groups, cancellationToken);

    public Task DeleteClientAsync(string username, CancellationToken cancellationToken = default) =>
        _dynSec.DeleteClientAsync(username, cancellationToken);

    public Task SetClientEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default) =>
        _dynSec.SetClientEnabledAsync(username, enabled, cancellationToken);

    public Task<IReadOnlyList<MqttRole>> ListRolesAsync(CancellationToken cancellationToken = default) =>
        _dynSec.ListRolesAsync(cancellationToken);

    public Task CreateRoleAsync(string name, string? description, IReadOnlyList<AclEntry> acls, CancellationToken cancellationToken = default) =>
        _dynSec.CreateRoleAsync(name, description, acls, cancellationToken);

    public Task DeleteRoleAsync(string name, CancellationToken cancellationToken = default) =>
        _dynSec.DeleteRoleAsync(name, cancellationToken);

    public Task<IReadOnlyList<MqttGroup>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        _dynSec.ListGroupsAsync(cancellationToken);

    public Task CreateGroupAsync(string name, string? description, IReadOnlyList<string> roleNames, IReadOnlyList<string> clientUsernames, CancellationToken cancellationToken = default) =>
        _dynSec.CreateGroupAsync(name, description, roleNames, clientUsernames, cancellationToken);

    public Task DeleteGroupAsync(string name, CancellationToken cancellationToken = default) =>
        _dynSec.DeleteGroupAsync(name, cancellationToken);

    public Task<string> ReadConfigAsync(CancellationToken cancellationToken = default) =>
        _config.ReadAsync(cancellationToken);

    public Task WriteConfigAsync(string content, CancellationToken cancellationToken = default) =>
        _config.WriteAsync(content, cancellationToken);

    public Task<IReadOnlyList<string>> GetRecentLogsAsync(int maxLines, CancellationToken cancellationToken = default) =>
        _logs.GetRecentLinesAsync(maxLines, cancellationToken);

    public Task<IReadOnlyList<TopicTreeNode>> GetTopicRootsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TopicTreeNode>>(_topics.GetRoots());

    public Task<AgentInfo> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var info = new AgentInfo
        {
            AgentVersion = "local",
            BrokerVersion = null,
            Uptime = TimeSpan.Zero,
            MqttConnected = _mqtt.IsConnected,
            TlsCertThumbprint = null,
        };
        return Task.FromResult(info);
    }

    public Task RestartBrokerAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Restart is not available for the in-process local broker."));
}
