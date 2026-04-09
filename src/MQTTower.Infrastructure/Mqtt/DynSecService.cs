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

    private async Task SendMutationAsync(JsonObject command, CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
        ThrowIfDynSecCommandError(response);
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
            list.Add(ParseClientJson(c));
        }

        return list;
    }

    public async Task<MqttClientInfo> GetClientAsync(string username, CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(
            new JsonObject { ["command"] = "getClient", ["username"] = username },
            cancellationToken).ConfigureAwait(false);
        ThrowIfDynSecCommandError(response);
        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("DynSec getClient: missing data.");
        }

        if (data.TryGetProperty("client", out var clientEl) && clientEl.ValueKind == JsonValueKind.Object)
        {
            return ParseClientJson(clientEl);
        }

        if (data.TryGetProperty("username", out _))
        {
            return ParseClientJson(data);
        }

        throw new InvalidOperationException("DynSec getClient: missing client object.");
    }

    public async Task CreateClientAsync(string username, string password, IReadOnlyList<string>? roles, IReadOnlyList<string>? groups, CancellationToken cancellationToken = default)
    {
        var roleItems = (roles ?? Array.Empty<string>())
            .Select(r => (JsonNode)new JsonObject { ["rolename"] = r, ["priority"] = -1 })
            .ToArray();
        var groupItems = (groups ?? Array.Empty<string>())
            .Select(g => (JsonNode)new JsonObject { ["groupname"] = g, ["priority"] = 1 })
            .ToArray();
        await SendMutationAsync(new JsonObject
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
        await SendMutationAsync(new JsonObject { ["command"] = "deleteClient", ["username"] = username }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetClientEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject { ["command"] = enabled ? "enableClient" : "disableClient", ["username"] = username }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetClientPasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject
        {
            ["command"] = "setClientPassword",
            ["username"] = username,
            ["password"] = password,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddClientRoleAsync(string username, string rolename, int priority = -1, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject
        {
            ["command"] = "addClientRole",
            ["username"] = username,
            ["rolename"] = rolename,
            ["priority"] = priority,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveClientRoleAsync(string username, string rolename, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject
        {
            ["command"] = "removeClientRole",
            ["username"] = username,
            ["rolename"] = rolename,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddGroupClientAsync(string groupname, string username, int priority = -1, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject
        {
            ["command"] = "addGroupClient",
            ["groupname"] = groupname,
            ["username"] = username,
            ["priority"] = priority,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveGroupClientAsync(string groupname, string username, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject
        {
            ["command"] = "removeGroupClient",
            ["groupname"] = groupname,
            ["username"] = username,
        }, cancellationToken).ConfigureAwait(false);
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
            list.Add(ParseRoleJson(r));
        }

        return list;
    }

    public async Task<MqttRole> GetRoleAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(
            new JsonObject { ["command"] = "getRole", ["rolename"] = name },
            cancellationToken).ConfigureAwait(false);
        ThrowIfDynSecCommandError(response);
        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("DynSec getRole: missing data.");
        }

        if (data.TryGetProperty("role", out var roleEl) && roleEl.ValueKind == JsonValueKind.Object)
        {
            return ParseRoleJson(roleEl);
        }

        if (data.TryGetProperty("rolename", out _))
        {
            return ParseRoleJson(data);
        }

        throw new InvalidOperationException("DynSec getRole: missing role object.");
    }

    public async Task CreateRoleAsync(string name, string? description, IReadOnlyList<AclEntry> acls, CancellationToken cancellationToken = default)
    {
        var flat = ExpandAclEntries(acls);
        var aclItems = flat.Select(a => (JsonNode)AclToJsonObject(a)).ToArray();
        await SendMutationAsync(new JsonObject
        {
            ["command"] = "createRole",
            ["rolename"] = name,
            ["textname"] = description ?? string.Empty,
            ["acls"] = new JsonArray(aclItems),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRoleAsync(string name, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject { ["command"] = "deleteRole", ["rolename"] = name }, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRoleAclAsync(string rolename, AclEntry acl, CancellationToken cancellationToken = default)
    {
        foreach (var expanded in ExpandAclEntries(new[] { acl }))
        {
            var o = AclToJsonObject(expanded);
            o["command"] = "addRoleACL";
            o["rolename"] = rolename;
            await SendMutationAsync(o, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveRoleAclAsync(string rolename, AclEntry acl, CancellationToken cancellationToken = default)
    {
        foreach (var expanded in ExpandAclEntries(new[] { acl }))
        {
            await SendMutationAsync(new JsonObject
            {
                ["command"] = "removeRoleACL",
                ["rolename"] = rolename,
                ["acltype"] = AclTypeToMosquittoString(expanded.AclType),
                ["topic"] = expanded.TopicPattern,
            }, cancellationToken).ConfigureAwait(false);
        }
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
            list.Add(ParseGroupJson(g));
        }

        return list;
    }

    public async Task<MqttGroup> GetGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(
            new JsonObject { ["command"] = "getGroup", ["groupname"] = name },
            cancellationToken).ConfigureAwait(false);
        ThrowIfDynSecCommandError(response);
        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("DynSec getGroup: missing data.");
        }

        if (data.TryGetProperty("group", out var groupEl) && groupEl.ValueKind == JsonValueKind.Object)
        {
            return ParseGroupJson(groupEl);
        }

        if (data.TryGetProperty("groupname", out _))
        {
            return ParseGroupJson(data);
        }

        throw new InvalidOperationException("DynSec getGroup: missing group object.");
    }

    public async Task CreateGroupAsync(string name, string? description, IReadOnlyList<string> roleNames, IReadOnlyList<string> clientUsernames, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject
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
        await SendMutationAsync(new JsonObject { ["command"] = "deleteGroup", ["groupname"] = name }, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddGroupRoleAsync(string groupname, string rolename, int priority = -1, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject
        {
            ["command"] = "addGroupRole",
            ["groupname"] = groupname,
            ["rolename"] = rolename,
            ["priority"] = priority,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveGroupRoleAsync(string groupname, string rolename, CancellationToken cancellationToken = default)
    {
        await SendMutationAsync(new JsonObject
        {
            ["command"] = "removeGroupRole",
            ["groupname"] = groupname,
            ["rolename"] = rolename,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject AclToJsonObject(AclEntry a)
    {
        return new JsonObject
        {
            ["acltype"] = AclTypeToMosquittoString(a.AclType),
            ["topic"] = a.TopicPattern,
            ["priority"] = a.Priority,
            ["allow"] = a.Allow,
        };
    }

    private static string AclTypeToMosquittoString(AclType t) =>
        t switch
        {
            AclType.Publish => "publishClientSend",
            AclType.Subscribe => "subscribePattern",
            AclType.PublishSubscribe => "publishClientSend",
            AclType.PublishReceive => "publishClientReceive",
            AclType.Unsubscribe => "unsubscribePattern",
            _ => "publishClientSend",
        };

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

    private static MqttClientInfo ParseClientJson(JsonElement c)
    {
        var username = c.TryGetProperty("username", out var u) ? u.GetString() ?? string.Empty : string.Empty;
        var info = new MqttClientInfo
        {
            Username = username,
            ClientId = c.TryGetProperty("clientid", out var cid) ? cid.GetString() : null,
            Enabled = !c.TryGetProperty("disabled", out var d) || !d.GetBoolean(),
        };

        if (c.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in roles.EnumerateArray())
            {
                if (r.TryGetProperty("rolename", out var rn))
                {
                    var name = rn.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        info.Roles.Add(name);
                    }
                }
            }
        }

        if (c.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in groups.EnumerateArray())
            {
                if (g.TryGetProperty("groupname", out var gn))
                {
                    var name = gn.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        info.Groups.Add(name);
                    }
                }
            }
        }

        return info;
    }

    private static MqttRole ParseRoleJson(JsonElement r)
    {
        var name = r.TryGetProperty("rolename", out var rn) ? rn.GetString() ?? string.Empty : string.Empty;
        var role = new MqttRole
        {
            Name = name,
            Description = r.TryGetProperty("textdescription", out var td)
                ? td.GetString()
                : r.TryGetProperty("textname", out var tn) ? tn.GetString() : null,
        };

        if (r.TryGetProperty("acls", out var acls) && acls.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in acls.EnumerateArray())
            {
                var entry = ParseAclJson(a);
                if (entry is not null)
                {
                    role.Acls.Add(entry);
                }
            }
        }

        return role;
    }

    private static AclEntry? ParseAclJson(JsonElement a)
    {
        var topic = a.TryGetProperty("topic", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var aclTypeStr = a.TryGetProperty("acltype", out var at) ? at.GetString() ?? string.Empty : string.Empty;
        var aclType = MosquittoStringToAclType(aclTypeStr);
        if (aclType is null)
        {
            return null;
        }

        return new AclEntry
        {
            TopicPattern = topic,
            AclType = aclType.Value,
            Allow = !a.TryGetProperty("allow", out var al) || al.GetBoolean(),
            Priority = a.TryGetProperty("priority", out var p) && p.TryGetInt32(out var pi) ? pi : 0,
        };
    }

    private static AclType? MosquittoStringToAclType(string s) =>
        s switch
        {
            "publishClientSend" => AclType.Publish,
            "subscribePattern" => AclType.Subscribe,
            "publishClientReceive" => AclType.PublishReceive,
            "unsubscribePattern" => AclType.Unsubscribe,
            _ => null,
        };

    private static MqttGroup ParseGroupJson(JsonElement g)
    {
        var name = g.TryGetProperty("groupname", out var gn) ? gn.GetString() ?? string.Empty : string.Empty;
        var group = new MqttGroup
        {
            Name = name,
            Description = g.TryGetProperty("textdescription", out var td)
                ? td.GetString()
                : g.TryGetProperty("textname", out var tn) ? tn.GetString() : null,
        };

        if (g.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in roles.EnumerateArray())
            {
                if (r.TryGetProperty("rolename", out var rn))
                {
                    var rnStr = rn.GetString();
                    if (!string.IsNullOrEmpty(rnStr))
                    {
                        group.RoleNames.Add(rnStr);
                    }
                }
            }
        }

        if (g.TryGetProperty("clients", out var clients) && clients.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in clients.EnumerateArray())
            {
                if (c.TryGetProperty("username", out var un))
                {
                    var u = un.GetString();
                    if (!string.IsNullOrEmpty(u))
                    {
                        group.ClientUsernames.Add(u);
                    }
                }
            }
        }

        return group;
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
