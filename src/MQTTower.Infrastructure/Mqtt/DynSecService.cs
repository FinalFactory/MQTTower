using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MqttAppMessage = MQTTower.Core.Mqtt.MqttAppMessage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Mqtt;

public sealed class DynSecService : IDynSecService
{
    private readonly IMqttPublisher _publisher;
    private readonly IMqttSubscriber _subscriber;
    private readonly MqttTowerOptions _options;
    private readonly ILogger<DynSecService> _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _subscribed;

    public DynSecService(
        IMqttPublisher publisher,
        IMqttSubscriber subscriber,
        IOptions<MqttTowerOptions> options,
        ILogger<DynSecService> logger)
    {
        _publisher = publisher;
        _subscriber = subscriber;
        _options = options.Value;
        _logger = logger;
    }

    private async Task EnsureResponseSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_subscribed)
            {
                return;
            }

            // Mosquitto publishes command results to e.g. $CONTROL/dynamic-security/v1/response, not the same
            // topic as the request — an exact subscription to .../v1 never receives them (see dynamic-security docs).
            var responseFilter = ControlResponseSubscriptionFilter(_options.ControlTopic);
            await _subscriber.SubscribeAsync(responseFilter, OnControlMessageAsync, cancellationToken).ConfigureAwait(false);
            _subscribed = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private Task OnControlMessageAsync(MqttAppMessage msg)
    {
        try
        {
            var json = JsonDocument.Parse(msg.Payload);
            var root = json.RootElement;

            // Mosquitto wraps replies in { "responses": [ { "correlationData": "...", "data": { ... } }, ... ] }.
            if (root.TryGetProperty("responses", out var responses) && responses.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in responses.EnumerateArray())
                {
                    if (!item.TryGetProperty("correlationData", out var corrEl))
                    {
                        continue;
                    }

                    var key = corrEl.GetString();
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    if (_pending.TryRemove(key, out var tcs))
                    {
                        tcs.TrySetResult(item.Clone());
                    }
                }

                return Task.CompletedTask;
            }

            // Single response / legacy shape with correlationData at root.
            if (root.TryGetProperty("correlationData", out var corr))
            {
                var key = corr.GetString();
                if (!string.IsNullOrEmpty(key) && _pending.TryRemove(key, out var tcs))
                {
                    tcs.TrySetResult(root.Clone());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DynSec response parse");
        }

        return Task.CompletedTask;
    }

    private async Task<JsonElement> SendCommandAsync(JsonObject command, CancellationToken cancellationToken)
    {
        await EnsureResponseSubscriptionAsync(cancellationToken).ConfigureAwait(false);
        var correlation = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlation] = tcs;

        try
        {
            // mosquitto_control_generic_callback expects root { "commands": [ ... ] }; correlationData is per-command
            // (see control__generic_handle_commands in Mosquitto control_common.c), not on the envelope root.
            command["correlationData"] = correlation;
            var envelope = new JsonObject
            {
                ["commands"] = new JsonArray { command },
            };
            var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);

            await _publisher.PublishAsync(_options.ControlTopic, payload, 1, false, cancellationToken).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(correlation, out _);
            if (ex is OperationCanceledException oce && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    oce,
                    "DynSec: no response within 15s (check Mosquitto, dynamic-security plugin, and that the agent MQTT user can subscribe to {ResponseFilter}; commands publish to {ControlTopic}).",
                    ControlResponseSubscriptionFilter(_options.ControlTopic),
                    _options.ControlTopic);
                throw new TimeoutException(
                    "No DynSec response from Mosquitto within 15 seconds.",
                    oce);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<MqttClientInfo>> ListClientsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(
            new JsonObject { ["command"] = "listClients", ["verbose"] = true },
            cancellationToken).ConfigureAwait(false);
        ThrowIfDynSecCommandError(response);
        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("clients", out var clients)
            || clients.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MqttClientInfo>();
        }

        var list = new List<MqttClientInfo>();
        foreach (var c in clients.EnumerateArray())
        {
            var username = c.TryGetProperty("username", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            list.Add(new MqttClientInfo
            {
                Username = username,
                ClientId = c.TryGetProperty("clientid", out var cid) ? cid.GetString() : null,
                Enabled = !c.TryGetProperty("disabled", out var d) || !d.GetBoolean(),
            });
        }

        return list;
    }

    public async Task CreateClientAsync(string username, string password, IReadOnlyList<string>? roles, IReadOnlyList<string>? groups, CancellationToken cancellationToken = default)
    {
        var roleItems = (roles ?? Array.Empty<string>())
            .Select(r => (JsonNode)new JsonObject { ["rolename"] = r, ["priority"] = -1 })
            .ToArray();
        var groupItems = (groups ?? Array.Empty<string>())
            .Select(g => (JsonNode)new JsonObject { ["groupname"] = g, ["priority"] = 1 })
            .ToArray();
        await SendCommandAsync(new JsonObject
        {
            ["command"] = "createClient",
            ["username"] = username,
            ["password"] = password,
            ["roles"] = new JsonArray(roleItems),
            ["groups"] = new JsonArray(groupItems),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteClientAsync(string username, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new JsonObject { ["command"] = "deleteClient", ["username"] = username }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetClientEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new JsonObject { ["command"] = enabled ? "enableClient" : "disableClient", ["username"] = username }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MqttRole>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(
            new JsonObject { ["command"] = "listRoles", ["verbose"] = true },
            cancellationToken).ConfigureAwait(false);
        ThrowIfDynSecCommandError(response);
        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("roles", out var roles)
            || roles.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MqttRole>();
        }

        var list = new List<MqttRole>();
        foreach (var r in roles.EnumerateArray())
        {
            var name = r.TryGetProperty("rolename", out var rn) ? rn.GetString() ?? string.Empty : string.Empty;
            list.Add(new MqttRole { Name = name });
        }

        return list;
    }

    public async Task CreateRoleAsync(string name, string? description, IReadOnlyList<AclEntry> acls, CancellationToken cancellationToken = default)
    {
        var flat = ExpandAclEntries(acls);
        var aclItems = flat.Select(a => (JsonNode)new JsonObject
        {
            ["acltype"] = a.AclType switch
            {
                AclType.Publish => "publishClientSend",
                AclType.Subscribe => "subscribePattern",
                AclType.PublishSubscribe => "publishClientSend",
                _ => "publishClientSend",
            },
            ["topic"] = a.TopicPattern,
            ["priority"] = a.Priority,
            ["allow"] = a.Allow,
        }).ToArray();
        await SendCommandAsync(new JsonObject
        {
            ["command"] = "createRole",
            ["rolename"] = name,
            ["textname"] = description ?? string.Empty,
            ["acls"] = new JsonArray(aclItems),
        }, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<AclEntry> ExpandAclEntries(IReadOnlyList<AclEntry> acls)
    {
        foreach (var a in acls)
        {
            if (a.AclType == AclType.PublishSubscribe)
            {
                yield return new AclEntry { TopicPattern = a.TopicPattern, AclType = AclType.Publish, Allow = a.Allow, Priority = a.Priority };
                yield return new AclEntry { TopicPattern = a.TopicPattern, AclType = AclType.Subscribe, Allow = a.Allow, Priority = a.Priority };
            }
            else
            {
                yield return a;
            }
        }
    }

    public async Task DeleteRoleAsync(string name, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new JsonObject { ["command"] = "deleteRole", ["rolename"] = name }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MqttGroup>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(
            new JsonObject { ["command"] = "listGroups", ["verbose"] = true },
            cancellationToken).ConfigureAwait(false);
        ThrowIfDynSecCommandError(response);
        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("groups", out var groups)
            || groups.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MqttGroup>();
        }

        var list = new List<MqttGroup>();
        foreach (var g in groups.EnumerateArray())
        {
            var name = g.TryGetProperty("groupname", out var gn) ? gn.GetString() ?? string.Empty : string.Empty;
            list.Add(new MqttGroup { Name = name });
        }

        return list;
    }

    public async Task CreateGroupAsync(string name, string? description, IReadOnlyList<string> roleNames, IReadOnlyList<string> clientUsernames, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new JsonObject
        {
            ["command"] = "createGroup",
            ["groupname"] = name,
            ["textname"] = description ?? string.Empty,
            ["roles"] = new JsonArray(roleNames.Select(r => (JsonNode)new JsonObject { ["rolename"] = r, ["priority"] = 1 }).ToArray<JsonNode>()),
            ["clients"] = new JsonArray(clientUsernames.Select(u => (JsonNode)new JsonObject { ["username"] = u, ["priority"] = 1 }).ToArray<JsonNode>()),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new JsonObject { ["command"] = "deleteGroup", ["groupname"] = name }, cancellationToken).ConfigureAwait(false);
    }

    private static void ThrowIfDynSecCommandError(JsonElement response)
    {
        if (response.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
        {
            throw new InvalidOperationException("DynSec command failed: " + err.GetString());
        }
    }

    /// <summary>
    /// Topic filter for receiving DynSec JSON replies. Mosquitto publishes responses under the control API path (e.g. .../v1/response), not on the publish topic alone.
    /// </summary>
    internal static string ControlResponseSubscriptionFilter(string controlTopic)
    {
        var t = controlTopic.Trim().TrimEnd('/');
        if (t.Length == 0)
        {
            return "$CONTROL/dynamic-security/v1/#";
        }

        return t.EndsWith("/#", StringComparison.Ordinal) || t.EndsWith("/+", StringComparison.Ordinal)
            ? t
            : t + "/#";
    }
}
