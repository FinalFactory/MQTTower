using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Core.Mqtt;
using MQTTower.Infrastructure.Automation;
using MQTTower.Infrastructure.Mqtt;

namespace MQTTower.Agent;

/// <summary>Subscribes to <c>#</c> on the co-located broker and evaluates watchers; notifies the dashboard via HTTP.</summary>
public sealed class AgentWatcherHostedService : IHostedService
{
    private readonly IMqttSubscriber _subscriber;
    private readonly AgentWatcherStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AgentOptions> _agentOptions;
    private readonly ILogger<AgentWatcherHostedService> _logger;

    public AgentWatcherHostedService(
        IMqttSubscriber subscriber,
        AgentWatcherStore store,
        IHttpClientFactory httpClientFactory,
        IOptions<AgentOptions> agentOptions,
        ILogger<AgentWatcherHostedService> logger)
    {
        _subscriber = subscriber;
        _store = store;
        _httpClientFactory = httpClientFactory;
        _agentOptions = agentOptions;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _subscriber.SubscribeAsync("#", OnMessageAsync, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent watcher MQTT subscribe failed");
            }
        }, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task OnMessageAsync(MqttAppMessage msg)
    {
        var text = Encoding.UTF8.GetString(msg.Payload);
        foreach (var w in _store.Snapshot())
        {
            if (!w.Enabled)
            {
                continue;
            }

            if (!MqttTopicMatcher.TopicMatches(msg.Topic, w.TopicPattern))
            {
                continue;
            }

            if (!WatcherConditionEvaluator.EvaluateCondition(w.Condition, text))
            {
                continue;
            }

            await NotifyDashboardAsync(w, msg.Topic, text).ConfigureAwait(false);
        }
    }

    private async Task NotifyDashboardAsync(TopicWatcher w, string topic, string text)
    {
        var opts = _agentOptions.Value;
        var baseUrl = opts.DashboardUrl?.Trim();
        var secret = opts.WatcherNotifySecret?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(new { w.Name, Topic = topic, text });
        var fullUrl = $"{baseUrl.TrimEnd('/')}/api/watcher-notify";
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var r = await client.PostAsJsonAsync(
                fullUrl,
                new WatcherNotifyDto(secret, payloadJson),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)).ConfigureAwait(false);
            if (!r.IsSuccessStatusCode)
            {
                _logger.LogWarning("Watcher notify failed with status {Status}", (int)r.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Watcher notify failed");
        }
    }

    private sealed record WatcherNotifyDto(string Secret, string PayloadJson);
}
