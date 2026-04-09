using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTower.Core.Models;
using MQTTower.Integration.Tests.Fixtures;
using MQTTower.Infrastructure.Mqtt;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace MQTTower.Integration.Tests;

[Trait("Category", "Integration")]
[Collection("Mosquitto")]
public sealed class DynSecIntegrationTests(MosquittoFixture fixture)
{
    private DynSecService CreateDynSec(MqttConnectionService mqtt) =>
        new(mqtt, mqtt, Options.Create(fixture.CreateOptions()), NullLogger<DynSecService>.Instance);

    [Fact]
    public async Task ListClients_includes_admin_user()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var clients = await dyn.ListClientsAsync();
        clients.Should().Contain(c => c.Username == MosquittoFixture.AdminUsername);
    }

    [Fact]
    public async Task Create_delete_client_roundtrip()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var name = $"u_{Guid.NewGuid():N}".Substring(0, 20);
        await dyn.CreateClientAsync(name, "pw-12345", Array.Empty<string>(), Array.Empty<string>());
        (await dyn.ListClientsAsync()).Should().Contain(c => c.Username == name);
        await dyn.DeleteClientAsync(name);
        (await dyn.ListClientsAsync()).Should().NotContain(c => c.Username == name);
    }

    [Fact]
    public async Task SetClientEnabled_toggles()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var name = $"e_{Guid.NewGuid():N}".Substring(0, 18);
        await dyn.CreateClientAsync(name, "pw-12345", Array.Empty<string>(), Array.Empty<string>());
        await dyn.SetClientEnabledAsync(name, false);
        (await dyn.ListClientsAsync()).First(c => c.Username == name).Enabled.Should().BeFalse();
        await dyn.SetClientEnabledAsync(name, true);
        (await dyn.ListClientsAsync()).First(c => c.Username == name).Enabled.Should().BeTrue();
        await dyn.DeleteClientAsync(name);
    }

    [Fact]
    public async Task Create_delete_role_with_acl()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var role = $"r_{Guid.NewGuid():N}".Substring(0, 18);
        await dyn.CreateRoleAsync(
            role,
            "it",
            new[]
            {
                new AclEntry { TopicPattern = "it/dynsec/#", AclType = AclType.Publish, Allow = true, Priority = 10 },
                new AclEntry { TopicPattern = "it/dynsec/#", AclType = AclType.Subscribe, Allow = true, Priority = 10 },
            });
        (await dyn.ListRolesAsync()).Should().Contain(r => r.Name == role);
        await dyn.DeleteRoleAsync(role);
        (await dyn.ListRolesAsync()).Should().NotContain(r => r.Name == role);
    }

    [Fact]
    public async Task Create_delete_group()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var g = $"g_{Guid.NewGuid():N}".Substring(0, 18);
        await dyn.CreateGroupAsync(g, "it", Array.Empty<string>(), Array.Empty<string>());
        (await dyn.ListGroupsAsync()).Should().Contain(x => x.Name == g);
        await dyn.DeleteGroupAsync(g);
        (await dyn.ListGroupsAsync()).Should().NotContain(x => x.Name == g);
    }

    [Fact]
    public async Task Client_without_roles_cannot_publish_to_application_topic()
    {
        await using var adminMqtt = fixture.CreateConnection();
        await adminMqtt.StartAsync();
        var dyn = CreateDynSec(adminMqtt);
        var name = $"lim_{Guid.NewGuid():N}".Substring(0, 16);
        await dyn.CreateClientAsync(name, "lim-pw-99", Array.Empty<string>(), Array.Empty<string>());

        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        var result = await client.ConnectAsync(
            new MqttClientOptionsBuilder()
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .WithTcpServer("127.0.0.1", fixture.MappedMqttPort)
                .WithCredentials(name, "lim-pw-99")
                .WithClientId($"it-lim-{Guid.NewGuid():N}"[..20])
                .WithCleanSession()
                .Build(),
            CancellationToken.None);

        result.ResultCode.Should().Be(MqttClientConnectResultCode.Success);

        var pubResult = await client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic("application/forbidden/topic")
                .WithPayload(new byte[] { 1 })
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(),
            CancellationToken.None);

        // MQTT v5 exposes broker denial via PUBACK reason code; v3.1.1 cannot distinguish here.
        pubResult.IsSuccess.Should().BeFalse("unprivileged client should not be allowed to publish application topics");
        try
        {
            await client.DisconnectAsync();
        }
        catch
        {
            /* already dropped */
        }
        await dyn.DeleteClientAsync(name);
    }

    [Fact]
    public async Task GetClient_returns_admin_with_roles()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var c = await dyn.GetClientAsync(MosquittoFixture.AdminUsername);
        c.Username.Should().Be(MosquittoFixture.AdminUsername);
        c.Roles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Add_remove_client_role_roundtrip()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var role = $"rt_{Guid.NewGuid():N}"[..16];
        await dyn.CreateRoleAsync(
            role,
            "t",
            new[]
            {
                new AclEntry { TopicPattern = "#", AclType = AclType.PublishSubscribe, Allow = true },
            });
        var user = $"ut_{Guid.NewGuid():N}"[..16];
        await dyn.CreateClientAsync(user, "pw-12345", Array.Empty<string>(), Array.Empty<string>());
        await dyn.AddClientRoleAsync(user, role);
        (await dyn.GetClientAsync(user)).Roles.Should().Contain(role);
        await dyn.RemoveClientRoleAsync(user, role);
        (await dyn.GetClientAsync(user)).Roles.Should().NotContain(role);
        await dyn.DeleteClientAsync(user);
        await dyn.DeleteRoleAsync(role);
    }

    [Fact]
    public async Task GetRole_and_GetGroup_return_details()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var role = $"gr_{Guid.NewGuid():N}"[..16];
        await dyn.CreateRoleAsync(
            role,
            "d",
            new[] { new AclEntry { TopicPattern = "x/#", AclType = AclType.Subscribe, Allow = true, Priority = 0 } });
        var r = await dyn.GetRoleAsync(role);
        r.Name.Should().Be(role);
        r.Acls.Should().NotBeEmpty();
        var g = $"gg_{Guid.NewGuid():N}"[..16];
        await dyn.CreateGroupAsync(g, "gd", Array.Empty<string>(), Array.Empty<string>());
        var gg = await dyn.GetGroupAsync(g);
        gg.Name.Should().Be(g);
        await dyn.DeleteGroupAsync(g);
        await dyn.DeleteRoleAsync(role);
    }

    [Fact]
    public async Task Add_remove_group_client_roundtrip()
    {
        await using var mqtt = fixture.CreateConnection();
        await mqtt.StartAsync();
        var dyn = CreateDynSec(mqtt);
        var g = $"gc_{Guid.NewGuid():N}"[..16];
        await dyn.CreateGroupAsync(g, "g", Array.Empty<string>(), Array.Empty<string>());
        var user = $"ugc_{Guid.NewGuid():N}"[..15];
        await dyn.CreateClientAsync(user, "pw-12345", Array.Empty<string>(), Array.Empty<string>());
        await dyn.AddGroupClientAsync(g, user);
        (await dyn.GetGroupAsync(g)).ClientUsernames.Should().Contain(user);
        await dyn.RemoveGroupClientAsync(g, user);
        (await dyn.GetGroupAsync(g)).ClientUsernames.Should().NotContain(user);
        await dyn.DeleteClientAsync(user);
        await dyn.DeleteGroupAsync(g);
    }
}
