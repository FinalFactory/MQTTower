using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Mqtt;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Integration.Tests.Fixtures;

/// <summary>
/// Starts eclipse-mosquitto with dynamic-security: initializes DynSec JSON, then runs Mosquitto.
/// Binds host directories for config, data, and log so tests can read <c>mosquitto.log</c> from disk.
/// </summary>
public sealed class MosquittoFixture : IAsyncLifetime
{
    /// <summary>Initial admin user created by <c>mosquitto_ctrl dynsec init</c> (see MOSQUITTO_DYNSEC_PASSWORD).</summary>
    public const string AdminUsername = "admin";

    public const string AdminPassword = "ItAdmin-9f3a2c1e";

    private string _hostRoot = string.Empty;
    private IContainer _container = null!;

    /// <summary>Usually <c>localhost</c>; use with <see cref="MappedMqttPort"/>.</summary>
    public string Hostname => _container.Hostname;

    public ushort MappedMqttPort { get; private set; }

    /// <summary>Path on the host to the log file written by Mosquitto (bind-mounted).</summary>
    public string HostLogFilePath => Path.Combine(_hostRoot, "log", "mosquitto.log");

    /// <summary>Path on the host to <c>mosquitto.conf</c> (bind-mounted).</summary>
    public string HostConfigPath => Path.Combine(_hostRoot, "config", "mosquitto.conf");

    public async Task InitializeAsync()
    {
        _hostRoot = Path.Combine(Path.GetTempPath(), "mqttower-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_hostRoot, "config"));
        Directory.CreateDirectory(Path.Combine(_hostRoot, "data"));
        Directory.CreateDirectory(Path.Combine(_hostRoot, "log"));

        var conf = """
            per_listener_settings false
            allow_anonymous false
            persistence true
            persistence_location /mosquitto/data/
            log_dest file /mosquitto/log/mosquitto.log
            log_type all
            listener 1883
            allow_zero_length_clientid false
            plugin /usr/lib/mosquitto_dynamic_security.so
            plugin_opt_config_file /mosquitto/data/dynamic-security.json
            """;
        await File.WriteAllTextAsync(Path.Combine(_hostRoot, "config", "mosquitto.conf"), conf).ConfigureAwait(false);

        // Non-interactive DynSec init: MOSQUITTO_DYNSEC_PASSWORD is applied to the admin user created by init.
        // See https://mosquitto.org/documentation/dynamic-security/ (Environment variable).
        // Idempotent init: do not re-run dynsec init on container restart or it would overwrite dynamic-security.json.
        var initAndRun =
            "mkdir -p /mosquitto/data /mosquitto/log && " +
            "if [ ! -f /mosquitto/data/dynamic-security.json ]; then " +
            "mosquitto_ctrl dynsec init /mosquitto/data/dynamic-security.json " + AdminUsername + "; fi && " +
            "exec mosquitto -c /mosquitto/config/mosquitto.conf";

        _container = new ContainerBuilder()
            .WithImage(new DockerImage("eclipse-mosquitto:2"))
            .WithEnvironment("MOSQUITTO_DYNSEC_PASSWORD", AdminPassword)
            .WithPortBinding(1883, true)
            .WithBindMount(Path.GetFullPath(Path.Combine(_hostRoot, "config")), "/mosquitto/config", AccessMode.ReadWrite)
            .WithBindMount(Path.GetFullPath(Path.Combine(_hostRoot, "data")), "/mosquitto/data", AccessMode.ReadWrite)
            .WithBindMount(Path.GetFullPath(Path.Combine(_hostRoot, "log")), "/mosquitto/log", AccessMode.ReadWrite)
            .WithEntrypoint("/bin/sh")
            .WithCommand("-c", initAndRun)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1883))
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        MappedMqttPort = _container.GetMappedPublicPort(1883);

        // Mosquitto 2.1+ assigns dynsec roles (super-admin, sys-observe, …) to admin but not the stock "client"
        // role, which carries publishClientSend on "#" for application topics. Attach that role to admin.
        await EnsureAdminCanPublishApplicationTopicsAsync().ConfigureAwait(false);
    }

    private async Task EnsureAdminCanPublishApplicationTopicsAsync()
    {
        var jsonPath = Path.Combine(_hostRoot, "data", "dynamic-security.json");
        await WaitForFileExistsAsync(jsonPath, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        const string applicationPublishRole = "client";

        // Prefer live broker (may not persist to the bind mount immediately on all setups).
        var addClientRoleScript =
            $"mosquitto_ctrl -u {AdminUsername} -P '{AdminPassword}' -h 127.0.0.1 -p 1883 dynsec addClientRole {AdminUsername} {applicationPublishRole}";
        var addRoleResult = await _container.ExecAsync(new[] { "/bin/sh", "-c", addClientRoleScript }).ConfigureAwait(false);
        if (addRoleResult.ExitCode != 0
            && !LooksLikeDuplicateAclMessage(addRoleResult.Stderr)
            && !LooksLikeDuplicateAclMessage(addRoleResult.Stdout))
        {
            throw new InvalidOperationException(
                $"mosquitto_ctrl addClientRole {AdminUsername} {applicationPublishRole} failed (exit {addRoleResult.ExitCode}). Stderr: {addRoleResult.Stderr}. Stdout: {addRoleResult.Stdout}.");
        }

        for (var i = 0; i < 50 && !AdminRoleHasPublishClientSendHash(jsonPath); i++)
        {
            await Task.Delay(200).ConfigureAwait(false);
        }

        if (AdminRoleHasPublishClientSendHash(jsonPath))
        {
            return;
        }

        // Ensure admin is linked to the stock "client" role on disk, then restart so Mosquitto reloads DynSec from the file.
        await PatchAdminClientRoleOnDiskAsync(jsonPath, applicationPublishRole).ConfigureAwait(false);
        await _container.StopAsync().ConfigureAwait(false);
        await _container.StartAsync().ConfigureAwait(false);
        MappedMqttPort = _container.GetMappedPublicPort(1883);
        await WaitForMqttPortOnHostAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        for (var i = 0; i < 50 && !AdminRoleHasPublishClientSendHash(jsonPath); i++)
        {
            await Task.Delay(200).ConfigureAwait(false);
        }

        if (AdminRoleHasPublishClientSendHash(jsonPath))
        {
            return;
        }

        // Last resort: add publishClientSend # to each role already assigned to admin.
        var roleNames = GetRoleNamesAssignedToUser(jsonPath, AdminUsername);
        foreach (var rolename in roleNames)
        {
            var addScript =
                $"mosquitto_ctrl -u {AdminUsername} -P '{AdminPassword}' -h 127.0.0.1 -p 1883 dynsec addRoleACL {rolename} publishClientSend '#' allow 10000";
            var addResult = await _container.ExecAsync(new[] { "/bin/sh", "-c", addScript }).ConfigureAwait(false);
            if (addResult.ExitCode != 0
                && !LooksLikeDuplicateAclMessage(addResult.Stderr)
                && !LooksLikeDuplicateAclMessage(addResult.Stdout))
            {
                throw new InvalidOperationException(
                    $"mosquitto_ctrl addRoleACL {rolename} publishClientSend # allow 10000 failed (exit {addResult.ExitCode}). Stderr: {addResult.Stderr}. Stdout: {addResult.Stdout}.");
            }
        }

        for (var i = 0; i < 50 && !AdminRoleHasPublishClientSendHash(jsonPath); i++)
        {
            await Task.Delay(200).ConfigureAwait(false);
        }

        if (AdminRoleHasPublishClientSendHash(jsonPath))
        {
            return;
        }

        var snippet = await File.ReadAllTextAsync(jsonPath).ConfigureAwait(false);
        if (snippet.Length > 4000)
        {
            snippet = snippet[..4000] + "…";
        }

        throw new InvalidOperationException(
            "Admin still has no publishClientSend allow for topic '#' after client role + patch + optional addRoleACL. dynamic-security.json (truncated): " + snippet);
    }

    private static bool AdminRoleHasPublishClientSendHash(string dynamicSecurityJsonPath)
    {
        if (!File.Exists(dynamicSecurityJsonPath))
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(dynamicSecurityJsonPath);
        }
        catch
        {
            return false;
        }

        using var doc = JsonDocument.Parse(text);
        if (!TryGetClientByUsername(doc.RootElement, AdminUsername, out var adminClient))
        {
            return false;
        }

        foreach (var roleName in GetRoleNamesForClient(adminClient))
        {
            if (!TryFindRoleByName(doc.RootElement, roleName, out var role))
            {
                continue;
            }

            if (!role.TryGetProperty("acls", out var acls) || acls.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var acl in acls.EnumerateArray())
            {
                var type = acl.TryGetProperty("acltype", out var t) ? t.GetString() : null;
                var topic = acl.TryGetProperty("topic", out var tp) ? tp.GetString() : null;
                var allow = acl.TryGetProperty("allow", out var al) && al.ValueKind == JsonValueKind.True;
                if (type == "publishClientSend" && allow && topic == "#")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindRoleByName(JsonElement root, string roleName, out JsonElement role)
    {
        role = default;
        if (!root.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var r in roles.EnumerateArray())
        {
            if (TryGetDynSecRoleName(r) == roleName)
            {
                role = r;
                return true;
            }
        }

        return false;
    }

    private static string? TryGetDynSecRoleName(JsonElement role)
    {
        if (role.TryGetProperty("rolename", out var rn))
        {
            return rn.GetString();
        }

        if (role.TryGetProperty("roleName", out var r2))
        {
            return r2.GetString();
        }

        if (role.TryGetProperty("name", out var r3))
        {
            return r3.GetString();
        }

        return null;
    }

    private static IReadOnlyList<string> GetRoleNamesAssignedToUser(string dynamicSecurityJsonPath, string username)
    {
        var text = File.ReadAllText(dynamicSecurityJsonPath);
        using var doc = JsonDocument.Parse(text);
        if (!TryGetClientByUsername(doc.RootElement, username, out var client))
        {
            throw new InvalidOperationException($"dynamic-security.json: no client with username '{username}'.");
        }

        var list = GetRoleNamesForClient(client).ToList();
        if (list.Count == 0)
        {
            throw new InvalidOperationException($"dynamic-security.json: client '{username}' has no roles assigned.");
        }

        return list;
    }

    private static bool TryGetClientByUsername(JsonElement root, string username, out JsonElement client)
    {
        client = default;
        if (!root.TryGetProperty("clients", out var clients) || clients.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var c in clients.EnumerateArray())
        {
            if (c.TryGetProperty("username", out var u) && u.GetString() == username)
            {
                client = c;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetRoleNamesForClient(JsonElement client)
    {
        if (!client.TryGetProperty("roles", out var rs) || rs.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var r in rs.EnumerateArray())
        {
            if (r.TryGetProperty("rolename", out var rn) && rn.GetString() is { } a)
            {
                yield return a;
            }
            else if (r.TryGetProperty("role", out var r2) && r2.GetString() is { } b)
            {
                yield return b;
            }
        }
    }

    private static async Task WaitForFileExistsAsync(string path, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!File.Exists(path))
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new InvalidOperationException($"Timed out waiting for {path}.");
            }

            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    /// <summary>Waits until the mapped host port accepts TCP (broker listening after restart).</summary>
    private async Task WaitForMqttPortOnHostAsync(TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", MappedMqttPort).ConfigureAwait(false);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(200).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"Timed out waiting for MQTT broker on 127.0.0.1:{MappedMqttPort}.");
    }

    private static async Task PatchAdminClientRoleOnDiskAsync(string dynamicSecurityJsonPath, string rolenameToAdd)
    {
        var text = await File.ReadAllTextAsync(dynamicSecurityJsonPath).ConfigureAwait(false);
        var root = JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException("dynamic-security.json: expected object root.");
        var clients = root["clients"] as JsonArray
            ?? throw new InvalidOperationException("dynamic-security.json: missing clients array.");

        JsonObject? adminNode = null;
        foreach (var node in clients)
        {
            if (node is JsonObject o && o["username"]?.GetValue<string>() == AdminUsername)
            {
                adminNode = o;
                break;
            }
        }

        if (adminNode is null)
        {
            throw new InvalidOperationException($"dynamic-security.json: no client '{AdminUsername}'.");
        }

        var roles = adminNode["roles"] as JsonArray ?? new JsonArray();
        adminNode["roles"] = roles;
        if (roles.Any(n => n is JsonObject ro && ro["rolename"]?.GetValue<string>() == rolenameToAdd))
        {
            await File.WriteAllTextAsync(
                    dynamicSecurityJsonPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }))
                .ConfigureAwait(false);
            return;
        }

        roles.Add(new JsonObject { ["rolename"] = rolenameToAdd });
        await File.WriteAllTextAsync(
                dynamicSecurityJsonPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
    }

    private static bool LooksLikeDuplicateAclMessage(string text) =>
        text.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
        || text.Contains("already", StringComparison.OrdinalIgnoreCase);

    public MqttTowerOptions CreateOptions(Action<MqttTowerOptions>? configure = null)
    {
        var o = new MqttTowerOptions
        {
            // Host port mapping is always reachable from the test host via loopback.
            BrokerHost = "127.0.0.1",
            BrokerPort = MappedMqttPort,
            BrokerUsername = AdminUsername,
            BrokerPassword = AdminPassword,
            MosquittoConfigPath = HostConfigPath,
            MosquittoLogPath = HostLogFilePath,
        };
        configure?.Invoke(o);
        return o;
    }

    public MqttConnectionService CreateConnection(MqttTowerOptions? options = null)
    {
        var o = options ?? CreateOptions();
        return new MqttConnectionService(Options.Create(o), Microsoft.Extensions.Logging.Abstractions.NullLogger<MqttConnectionService>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }

        try
        {
            if (!string.IsNullOrEmpty(_hostRoot) && Directory.Exists(_hostRoot))
            {
                Directory.Delete(_hostRoot, recursive: true);
            }
        }
        catch
        {
            /* best-effort */
        }
    }
}
