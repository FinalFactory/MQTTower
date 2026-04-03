using System.Collections.Concurrent;
using System.Text.Json;
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

    private async Task<JsonElement> SendCommandAsync(object command, CancellationToken cancellationToken)
    {
        await EnsureResponseSubscriptionAsync(cancellationToken).ConfigureAwait(false);
        var correlation = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlation] = tcs;

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(command, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var doc = JsonDocument.Parse(payload);
            var dict = new Dictionary<string, JsonElement>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                dict[p.Name] = p.Value.Clone();
            }

            dict["correlationData"] = JsonSerializer.SerializeToElement(correlation);
            var merged = JsonSerializer.SerializeToUtf8Bytes(dict);

            await _publisher.PublishAsync(_options.ControlTopic, merged, 1, false, cancellationToken).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(correlation, out _);
            throw;
        }
    }

    public async Task<IReadOnlyList<MqttClientInfo>> ListClientsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new { command = "listClients" }, cancellationToken).ConfigureAwait(false);
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
        await SendCommandAsync(new
        {
            command = "createClient",
            username,
            password,
            roles = roles ?? Array.Empty<string>(),
            groups = groups ?? Array.Empty<string>(),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteClientAsync(string username, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new { command = "deleteClient", username }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetClientEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new { command = enabled ? "enableClient" : "disableClient", username }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MqttRole>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new { command = "listRoles" }, cancellationToken).ConfigureAwait(false);
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
        await SendCommandAsync(new
        {
            command = "createRole",
            rolename = name,
            textname = description ?? string.Empty,
            acls = flat.Select(a => new
            {
                acltype = a.AclType switch
                {
                    AclType.Publish => "publishClientSend",
                    AclType.Subscribe => "subscribePattern",
                    AclType.PublishSubscribe => "publishClientSend",
                    _ => "publishClientSend",
                },
                topic = a.TopicPattern,
                priority = a.Priority,
                allow = a.Allow,
            }),
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
        await SendCommandAsync(new { command = "deleteRole", rolename = name }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MqttGroup>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new { command = "listGroups" }, cancellationToken).ConfigureAwait(false);
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
        await SendCommandAsync(new
        {
            command = "createGroup",
            groupname = name,
            textname = description ?? string.Empty,
            roles = roleNames,
            clients = clientUsernames,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new { command = "deleteGroup", groupname = name }, cancellationToken).ConfigureAwait(false);
    }
}
