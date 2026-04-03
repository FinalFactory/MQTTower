using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MQTTower.Core.Models;
using MQTTower.Core.TopicExplorer;
using MQTTower.Infrastructure.Agents;

namespace MQTTower.Infrastructure.Tests;

public sealed class AgentHttpClientSseTests
{
    [Fact]
    public async Task RunStatsStreamLoopAsync_deserializes_data_line_to_BrokerStats()
    {
        var stats = new BrokerStats { ConnectedClients = 42, ActiveTopics = 3 };
        var json = JsonSerializer.Serialize(stats, AgentHttpClientSseTestHelpers.Json);
        var body = $"data: {json}\n\n";

        using var http = new HttpClient(new SseHttpMessageHandler(body)) { BaseAddress = new Uri("http://test/") };
        using var client = new AgentHttpClient(Guid.NewGuid(), http);

        BrokerStats? received = null;
        await client.RunStatsStreamLoopAsync(
            s =>
            {
                received = s;
                return Task.CompletedTask;
            });

        received.Should().NotBeNull();
        received!.ConnectedClients.Should().Be(42);
        received.ActiveTopics.Should().Be(3);
    }

    [Fact]
    public async Task RunStatsStreamLoopAsync_no_callback_when_status_not_success()
    {
        using var http = new HttpClient(new FixedStatusHttpMessageHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri("http://test/") };
        using var client = new AgentHttpClient(Guid.NewGuid(), http);

        var called = false;
        await client.RunStatsStreamLoopAsync(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            });

        called.Should().BeFalse();
    }

    [Fact]
    public async Task RunLogsStreamLoopAsync_deserializes_json_array()
    {
        var lines = new[] { "a", "b" };
        var json = JsonSerializer.Serialize(lines, AgentHttpClientSseTestHelpers.Json);
        var body = $"data: {json}\n\n";

        using var http = new HttpClient(new SseHttpMessageHandler(body)) { BaseAddress = new Uri("http://test/") };
        using var client = new AgentHttpClient(Guid.NewGuid(), http);

        IReadOnlyList<string>? received = null;
        await client.RunLogsStreamLoopAsync(
            l =>
            {
                received = l;
                return Task.CompletedTask;
            });

        received.Should().NotBeNull();
        received!.Should().Equal("a", "b");
    }

    [Fact]
    public async Task RunTopicsStreamLoopAsync_deserializes_topic_roots()
    {
        var expectedRoots = new List<TopicTreeNode>
        {
            new() { Segment = "s", FullTopic = "s", MessageCount = 1 },
        };
        var json = JsonSerializer.Serialize(expectedRoots, AgentHttpClientSseTestHelpers.Json);
        var body = $"data: {json}\n\n";

        using var http = new HttpClient(new SseHttpMessageHandler(body)) { BaseAddress = new Uri("http://test/") };
        using var client = new AgentHttpClient(Guid.NewGuid(), http);

        IReadOnlyList<TopicTreeNode>? received = null;
        await client.RunTopicsStreamLoopAsync(
            r =>
            {
                received = r;
                return Task.CompletedTask;
            });

        received.Should().NotBeNull();
        var got = received!;
        got.Should().HaveCount(1);
        got[0].Segment.Should().Be("s");
        got[0].FullTopic.Should().Be("s");
    }
}

internal static class AgentHttpClientSseTestHelpers
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

internal sealed class SseHttpMessageHandler : HttpMessageHandler
{
    private readonly string _sseBody;

    public SseHttpMessageHandler(string sseBody) => _sseBody = sseBody;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ms = new MemoryStream(Encoding.UTF8.GetBytes(_sseBody));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(ms),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return Task.FromResult(response);
    }
}

internal sealed class FixedStatusHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;

    public FixedStatusHttpMessageHandler(HttpStatusCode status) => _status = status;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(_status));
}
