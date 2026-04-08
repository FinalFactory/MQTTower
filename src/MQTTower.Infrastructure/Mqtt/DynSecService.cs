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

            await _subscriber.SubscribeAsync(_options.ControlTopic, OnControlMessageAsync, cancellationToken).ConfigureAwait(false);
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
            if (!json.RootElement.TryGetProperty("correlationData", out var corr))
            {
                return Task.CompletedTask;
            }

            var key = corr.GetString();
            if (string.IsNullOrEmpty(key))
            {
                return Task.CompletedTask;
            }

            if (_pending.TryRemove(key, out var tcs))
            {
                tcs.TrySetResult(json.RootElement.Clone());
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
            var payload = JsonSerializer.SerializeToUtf8Bytes(command);

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
                _logger.LogDebug(
                    oce,
                    "DynSec: no response within 15s (check Mosquitto, dynamic-security plugin, and MQTT subscription on {ControlTopic}).",
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
        var response = await SendCommandAsync(new JsonObject { ["command"] = "listClients" }, cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("clients", out var clients) || clients.ValueKind != JsonValueKind.Array)
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
        var roleItems = (roles ?? Array.Empty<string>()).Select(x => JsonValue.Create(x)).ToArray<JsonNode>();
        var groupItems = (groups ?? Array.Empty<string>()).Select(x => JsonValue.Create(x)).ToArray<JsonNode>();
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
        var response = await SendCommandAsync(new JsonObject { ["command"] = "listRoles" }, cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
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
        var response = await SendCommandAsync(new JsonObject { ["command"] = "listGroups" }, cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
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
            ["roles"] = new JsonArray(roleNames.Select(x => JsonValue.Create(x)).ToArray<JsonNode>()),
            ["clients"] = new JsonArray(clientUsernames.Select(x => JsonValue.Create(x)).ToArray<JsonNode>()),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new JsonObject { ["command"] = "deleteGroup", ["groupname"] = name }, cancellationToken).ConfigureAwait(false);
    }
}
