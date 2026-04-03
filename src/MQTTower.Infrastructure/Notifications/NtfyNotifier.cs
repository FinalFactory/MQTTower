using System.Net.Http;
using System.Text;
using MQTTower.Core.Interfaces;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Notifications;

public sealed class NtfyNotifier : INotificationChannel
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly MqttTowerOptions _options;

    public NtfyNotifier(IHttpClientFactory httpFactory, IOptions<MqttTowerOptions> options)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
    }

    public string ChannelId => "ntfy";

    public async Task SendAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.NtfyBaseUrl) || string.IsNullOrWhiteSpace(_options.NtfyTopic))
        {
            return;
        }

        var client = _httpFactory.CreateClient(nameof(NtfyNotifier));
        var url = $"{_options.NtfyBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(_options.NtfyTopic)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Title", title);
        req.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }
}
