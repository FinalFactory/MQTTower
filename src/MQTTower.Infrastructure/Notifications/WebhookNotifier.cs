using System.Net.Http.Json;
using MQTTower.Core.Interfaces;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Notifications;

public sealed class WebhookNotifier : INotificationChannel
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly MqttTowerOptions _options;

    public WebhookNotifier(IHttpClientFactory httpFactory, IOptions<MqttTowerOptions> options)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
    }

    public string ChannelId => "webhook";

    public async Task SendAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            return;
        }

        var client = _httpFactory.CreateClient(nameof(WebhookNotifier));
        var payload = new { title, body };
        await client.PostAsJsonAsync(_options.WebhookUrl, payload, cancellationToken).ConfigureAwait(false);
    }
}
