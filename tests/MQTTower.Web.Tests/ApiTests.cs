using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using MQTTower.Web;

namespace MQTTower.Web.Tests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var res = await _client.GetAsync("/api/health");
        res.IsSuccessStatusCode.Should().BeTrue();
    }
}
