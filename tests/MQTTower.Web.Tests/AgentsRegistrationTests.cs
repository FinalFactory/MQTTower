using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MQTTower.Core.Interfaces;
using MQTTower.Infrastructure.Data;
using MQTTower.Web;

namespace MQTTower.Web.Tests;

public sealed class AgentsRegistrationTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        string? registrationSecret = null,
        string? localAgentUrl = null,
        string? localAgentApiKey = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mqttower_regtest_{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={dbPath}",
                };
                if (registrationSecret is not null)
                {
                    dict["MQTTower:RegistrationSecret"] = registrationSecret;
                }

                if (localAgentUrl is not null)
                {
                    dict["MQTTower:LocalAgentUrl"] = localAgentUrl;
                }

                if (localAgentApiKey is not null)
                {
                    dict["MQTTower:LocalAgentApiKey"] = localAgentApiKey;
                }

                config.AddInMemoryCollection(dict);
            });
        });
    }

    [Fact]
    public async Task Register_returns_503_when_secret_not_configured_and_token_whitespace_only()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/agents/register", new
        {
            registrationToken = " ",
            agentUrl = "https://agent/",
            apiKey = "k",
            name = "A",
        });
        res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Register_succeeds_with_one_time_token_when_secret_not_configured()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        string plaintext;
        using (var scope = factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<IRegistrationTokenService>();
            plaintext = await tokens.MintAsync(null);
        }

        var res = await client.PostAsJsonAsync("/api/agents/register", new
        {
            registrationToken = plaintext,
            agentUrl = "https://one-time-agent/",
            apiKey = "k",
            name = "OT",
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_returns_401_when_token_wrong()
    {
        await using var factory = CreateFactory("correct-secret");
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/agents/register", new
        {
            registrationToken = "wrong",
            agentUrl = "https://agent/",
            apiKey = "k",
            name = "A",
        });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_returns_200_and_persists_profile_when_valid()
    {
        const string secret = "integration-test-secret";
        await using var factory = CreateFactory(secret);
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/agents/register", new
        {
            registrationToken = secret,
            agentUrl = "https://remote-agent:5100/",
            apiKey = "api-key-value",
            name = "Remote broker",
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetGuid();
        id.Should().NotBeEmpty();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.BrokerProfiles.AsNoTracking().SingleAsync(x => x.Id == id);
        row.Name.Should().Be("Remote broker");
        row.ApiKey.Should().Be("api-key-value");
    }

    [Fact]
    public async Task Register_same_agent_url_updates_existing_row()
    {
        const string secret = "integration-test-secret";
        await using var factory = CreateFactory(secret);
        var client = factory.CreateClient();
        var body = new
        {
            registrationToken = secret,
            agentUrl = "https://same-agent/",
            apiKey = "first-key",
            name = "First",
        };
        var res1 = await client.PostAsJsonAsync("/api/agents/register", body);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var id1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res2 = await client.PostAsJsonAsync("/api/agents/register", new
        {
            registrationToken = secret,
            agentUrl = "https://same-agent/",
            apiKey = "second-key",
            name = "Second",
        });
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var id2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        id2.Should().Be(id1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.BrokerProfiles.AsNoTracking().SingleAsync(x => x.Id == id1);
        row.ApiKey.Should().Be("second-key");
        row.Name.Should().Be("Second");
    }

    [Fact]
    public async Task Register_merges_into_local_broker_profile_when_UseLocalServices()
    {
        const string secret = "integration-test-secret";
        const string agentUrl = "http://127.0.0.1:5080";
        await using var factory = CreateFactory(secret, agentUrl, "seed-key");
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/agents/register", new
        {
            registrationToken = secret,
            agentUrl,
            apiKey = "rotated-key",
            name = "Host",
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.BrokerProfiles.AsNoTracking().SingleAsync(x => x.Id == id);
        row.UseLocalServices.Should().BeTrue();
        row.Approved.Should().BeTrue();
        row.ApiKey.Should().Be("rotated-key");
        row.Name.Should().Be("Host");
    }
}
