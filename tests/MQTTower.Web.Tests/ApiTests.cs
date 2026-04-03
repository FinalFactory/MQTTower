using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MQTTower.Web;

namespace MQTTower.Web.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mqttower_apitest_{Guid.NewGuid():N}.db");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={dbPath}",
                });
            });
        });

        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/health");
        res.IsSuccessStatusCode.Should().BeTrue();
    }
}
