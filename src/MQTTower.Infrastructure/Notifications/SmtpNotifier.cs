using System.Net.Mail;
using MQTTower.Core.Interfaces;
using Microsoft.Extensions.Options;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Infrastructure.Notifications;

public sealed class SmtpNotifier : INotificationChannel
{
    private readonly MqttTowerOptions _options;

    public SmtpNotifier(IOptions<MqttTowerOptions> options)
    {
        _options = options.Value;
    }

    public string ChannelId => "smtp";

    public async Task SendAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost)
            || string.IsNullOrWhiteSpace(_options.SmtpFrom)
            || string.IsNullOrWhiteSpace(_options.SmtpTo))
        {
            return;
        }

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort);
        using var msg = new MailMessage(_options.SmtpFrom, _options.SmtpTo, title, body);
        await client.SendMailAsync(msg, cancellationToken).ConfigureAwait(false);
    }
}
