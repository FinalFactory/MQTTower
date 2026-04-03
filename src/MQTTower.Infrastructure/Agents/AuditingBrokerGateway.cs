using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Core.TopicExplorer;

namespace MQTTower.Infrastructure.Agents;

public sealed class AuditingBrokerGatewayFactory : IBrokerGatewayFactory
{
    private readonly AgentGatewayFactory _inner;
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditingBrokerGatewayFactory(AgentGatewayFactory inner, IServiceScopeFactory scopeFactory)
    {
        _inner = inner;
        _scopeFactory = scopeFactory;
    }

    public IBrokerGateway Create(BrokerProfile broker)
    {
        var inner = _inner.Create(broker);
        var scope = _scopeFactory.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuditingBrokerGateway>>();
        return new AuditingBrokerGateway(inner, audit, broker.Id, scope, logger);
    }
}

public sealed class AuditingBrokerGateway : IBrokerGateway, IDisposable
{
    private readonly IBrokerGateway _inner;
    private readonly IAuditLog _audit;
    private readonly Guid _brokerId;
    private readonly IServiceScope _scope;
    private readonly ILogger<AuditingBrokerGateway> _logger;

    public AuditingBrokerGateway(IBrokerGateway inner, IAuditLog audit, Guid brokerId, IServiceScope scope, ILogger<AuditingBrokerGateway> logger)
    {
        _inner = inner;
        _audit = audit;
        _brokerId = brokerId;
        _scope = scope;
        _logger = logger;
    }

    public Guid BrokerId => _inner.BrokerId;

    public void Dispose()
    {
        (_inner as IDisposable)?.Dispose();
        _scope.Dispose();
    }

    public Task<BrokerStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        RunAsync("GetStats", () => _inner.GetStatsAsync(cancellationToken));

    public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qos, bool retain, CancellationToken cancellationToken = default) =>
        RunAsync("Publish", () => _inner.PublishAsync(topic, payload, qos, retain, cancellationToken));

    public Task<IReadOnlyList<MqttClientInfo>> ListClientsAsync(CancellationToken cancellationToken = default) =>
        RunAsync("ListClients", () => _inner.ListClientsAsync(cancellationToken));

    public Task CreateClientAsync(string username, string password, IReadOnlyList<string>? roles, IReadOnlyList<string>? groups, CancellationToken cancellationToken = default) =>
        RunAsync("CreateClient", () => _inner.CreateClientAsync(username, password, roles, groups, cancellationToken));

    public Task DeleteClientAsync(string username, CancellationToken cancellationToken = default) =>
        RunAsync("DeleteClient", () => _inner.DeleteClientAsync(username, cancellationToken));

    public Task SetClientEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default) =>
        RunAsync("SetClientEnabled", () => _inner.SetClientEnabledAsync(username, enabled, cancellationToken));

    public Task<IReadOnlyList<MqttRole>> ListRolesAsync(CancellationToken cancellationToken = default) =>
        RunAsync("ListRoles", () => _inner.ListRolesAsync(cancellationToken));

    public Task CreateRoleAsync(string name, string? description, IReadOnlyList<AclEntry> acls, CancellationToken cancellationToken = default) =>
        RunAsync("CreateRole", () => _inner.CreateRoleAsync(name, description, acls, cancellationToken));

    public Task DeleteRoleAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync("DeleteRole", () => _inner.DeleteRoleAsync(name, cancellationToken));

    public Task<IReadOnlyList<MqttGroup>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        RunAsync("ListGroups", () => _inner.ListGroupsAsync(cancellationToken));

    public Task CreateGroupAsync(string name, string? description, IReadOnlyList<string> roleNames, IReadOnlyList<string> clientUsernames, CancellationToken cancellationToken = default) =>
        RunAsync("CreateGroup", () => _inner.CreateGroupAsync(name, description, roleNames, clientUsernames, cancellationToken));

    public Task DeleteGroupAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync("DeleteGroup", () => _inner.DeleteGroupAsync(name, cancellationToken));

    public Task<string> ReadConfigAsync(CancellationToken cancellationToken = default) =>
        RunAsync("ReadConfig", () => _inner.ReadConfigAsync(cancellationToken));

    public Task WriteConfigAsync(string content, CancellationToken cancellationToken = default) =>
        RunAsync("WriteConfig", () => _inner.WriteConfigAsync(content, cancellationToken));

    public Task<IReadOnlyList<string>> GetRecentLogsAsync(int maxLines, CancellationToken cancellationToken = default) =>
        RunAsync("GetRecentLogs", () => _inner.GetRecentLogsAsync(maxLines, cancellationToken));

    public Task<IReadOnlyList<TopicTreeNode>> GetTopicRootsAsync(CancellationToken cancellationToken = default) =>
        RunAsync("GetTopicRoots", () => _inner.GetTopicRootsAsync(cancellationToken));

    public Task<AgentInfo> GetHealthAsync(CancellationToken cancellationToken = default) =>
        RunAsync("GetHealth", () => _inner.GetHealthAsync(cancellationToken));

    public Task RestartBrokerAsync(CancellationToken cancellationToken = default) =>
        RunAsync("RestartBroker", () => _inner.RestartBrokerAsync(cancellationToken));

    private async Task<T> RunAsync<T>(string op, Func<Task<T>> fn)
    {
        try
        {
            var r = await fn().ConfigureAwait(false);
            await AppendAsync(op, success: true).ConfigureAwait(false);
            return r;
        }
        catch (Exception ex)
        {
            await AppendAsync(op, success: false, ex.Message).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunAsync(string op, Func<Task> fn)
    {
        try
        {
            await fn().ConfigureAwait(false);
            await AppendAsync(op, success: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await AppendAsync(op, success: false, ex.Message).ConfigureAwait(false);
            throw;
        }
    }

    private async Task AppendAsync(string op, bool success, string? error = null)
    {
        try
        {
            await _audit.AppendAsync(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                UserName = "BrokerGateway",
                Action = op,
                EntityType = "BrokerGateway",
                EntityName = _brokerId.ToString("D"),
                Details = success ? $"ok; brokerId={_brokerId:D}" : $"fail; brokerId={_brokerId:D}; {error}",
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit append failed for broker gateway op {Op}; continuing without audit", op);
        }
    }
}
