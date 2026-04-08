using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Core.TopicExplorer;

namespace MQTTower.Infrastructure.Agents;

public sealed class AgentHttpClient : IBrokerGateway, IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Guid _brokerId;
    private readonly HttpClient _http;

    public AgentHttpClient(Guid brokerId, HttpClient http)
    {
        _brokerId = brokerId;
        _http = http;
    }

    public Guid BrokerId => _brokerId;

    public void Dispose() => _http.Dispose();

    public async Task<BrokerStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var r = await _http.GetAsync("api/stats", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<BrokerStats>(Json, cancellationToken).ConfigureAwait(false) ?? new BrokerStats();
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qos, bool retain, CancellationToken cancellationToken = default)
    {
        var body = new PublishDto(
            topic,
            Encoding.UTF8.GetString(payload.Span),
            qos,
            retain);
        var r = await _http.PostAsJsonAsync("api/mqtt/publish", body, Json, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MqttClientInfo>> ListClientsAsync(CancellationToken cancellationToken = default)
    {
        var r = await _http.GetAsync("api/dynsec/clients", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<List<MqttClientInfo>>(Json, cancellationToken).ConfigureAwait(false) ?? new List<MqttClientInfo>();
    }

    public async Task CreateClientAsync(string username, string password, IReadOnlyList<string>? roles, IReadOnlyList<string>? groups, CancellationToken cancellationToken = default)
    {
        var body = new CreateClientDto(username, password, roles?.ToList(), groups?.ToList());
        var r = await _http.PostAsJsonAsync("api/dynsec/clients", body, Json, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteClientAsync(string username, CancellationToken cancellationToken = default)
    {
        var r = await _http.DeleteAsync($"api/dynsec/clients/{Uri.EscapeDataString(username)}", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetClientEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default)
    {
        var r = await _http.PutAsJsonAsync(
            $"api/dynsec/clients/{Uri.EscapeDataString(username)}/enabled",
            new EnabledDto(enabled),
            Json,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MqttRole>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var r = await _http.GetAsync("api/dynsec/roles", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<List<MqttRole>>(Json, cancellationToken).ConfigureAwait(false) ?? new List<MqttRole>();
    }

    public async Task CreateRoleAsync(string name, string? description, IReadOnlyList<AclEntry> acls, CancellationToken cancellationToken = default)
    {
        var body = new CreateRoleDto(name, description, acls.ToList());
        var r = await _http.PostAsJsonAsync("api/dynsec/roles", body, Json, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRoleAsync(string name, CancellationToken cancellationToken = default)
    {
        var r = await _http.DeleteAsync($"api/dynsec/roles/{Uri.EscapeDataString(name)}", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MqttGroup>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var r = await _http.GetAsync("api/dynsec/groups", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<List<MqttGroup>>(Json, cancellationToken).ConfigureAwait(false) ?? new List<MqttGroup>();
    }

    public async Task CreateGroupAsync(string name, string? description, IReadOnlyList<string> roleNames, IReadOnlyList<string> clientUsernames, CancellationToken cancellationToken = default)
    {
        var body = new CreateGroupDto(name, description, roleNames.ToList(), clientUsernames.ToList());
        var r = await _http.PostAsJsonAsync("api/dynsec/groups", body, Json, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        var r = await _http.DeleteAsync($"api/dynsec/groups/{Uri.EscapeDataString(name)}", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ReadConfigAsync(CancellationToken cancellationToken = default)
    {
        var r = await _http.GetAsync("api/config", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
        return await r.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteConfigAsync(string content, CancellationToken cancellationToken = default)
    {
        var r = await _http.PutAsync("api/config", new StringContent(content, Encoding.UTF8, "text/plain"), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetRecentLogsAsync(int maxLines, CancellationToken cancellationToken = default)
    {
        var r = await _http.GetAsync($"api/logs?lines={maxLines}", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<List<string>>(Json, cancellationToken).ConfigureAwait(false) ?? new List<string>();
    }

    public async Task<IReadOnlyList<TopicTreeNode>> GetTopicRootsAsync(CancellationToken cancellationToken = default)
    {
        var r = await _http.GetAsync("api/topics", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<List<TopicTreeNode>>(Json, cancellationToken).ConfigureAwait(false) ?? new List<TopicTreeNode>();
    }

    public async Task<AgentInfo> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "health");
        var r = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<AgentInfo>(Json, cancellationToken).ConfigureAwait(false) ?? new AgentInfo();
    }

    public async Task RestartBrokerAsync(CancellationToken cancellationToken = default)
    {
        var r = await _http.PostAsync("api/broker/restart", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(r, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pushes a new API key to the agent (uses current <see cref="BrokerProfile.ApiKey"/> as auth header).</summary>
    public async Task<HttpResponseMessage> PushApiKeyAsync(string newApiKey, CancellationToken cancellationToken = default)
    {
        return await _http.PostAsJsonAsync("api/agent/key", new SetApiKeyDto(newApiKey), Json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the agent-side watcher definitions (evaluated on the agent host).</summary>
    public async Task<HttpResponseMessage> SyncWatchersAsync(IReadOnlyList<TopicWatcher> watchers, CancellationToken cancellationToken = default)
    {
        return await _http.PutAsJsonAsync("api/watchers/sync", watchers.ToList(), Json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the agent <c>/api/stats/stream</c> SSE endpoint until cancelled or the stream ends.</summary>
    public async Task RunStatsStreamLoopAsync(Func<BrokerStats, Task> onStats, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/stats/stream");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line.AsSpan(5).Trim();
            var stats = JsonSerializer.Deserialize<BrokerStats>(json, Json);
            if (stats is not null)
            {
                await onStats(stats).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads the agent <c>/api/logs/stream</c> SSE endpoint until cancelled or the stream ends.</summary>
    public async Task RunLogsStreamLoopAsync(Func<IReadOnlyList<string>, Task> onLines, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/logs/stream");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line.AsSpan(5).Trim();
            var lines = JsonSerializer.Deserialize<List<string>>(json, Json);
            if (lines is not null)
            {
                await onLines(lines).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads the agent <c>/api/topics/stream</c> SSE endpoint until cancelled or the stream ends.</summary>
    public async Task RunTopicsStreamLoopAsync(Func<IReadOnlyList<TopicTreeNode>, Task> onRoots, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/topics/stream");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line.AsSpan(5).Trim();
            var roots = JsonSerializer.Deserialize<List<TopicTreeNode>>(json, Json);
            if (roots is not null)
            {
                await onRoots(roots).ConfigureAwait(false);
            }
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await TryReadAgentErrorBodyAsync(response, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        throw new HttpRequestException(message, inner: null, statusCode: response.StatusCode);
    }

    private static async Task<string?> TryReadAgentErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            {
                return err.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private sealed record SetApiKeyDto(string ApiKey);
    private sealed record EnabledDto(bool Enabled);
    private sealed record PublishDto(string Topic, string? Payload, int Qos, bool Retain);
    private sealed record CreateClientDto(string Username, string Password, List<string>? Roles, List<string>? Groups);
    private sealed record CreateRoleDto(string Name, string? Description, List<AclEntry> Acls);
    private sealed record CreateGroupDto(string Name, string? Description, List<string> RoleNames, List<string> ClientUsernames);
}
