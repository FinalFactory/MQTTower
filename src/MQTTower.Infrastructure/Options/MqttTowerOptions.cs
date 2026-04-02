namespace MQTTower.Infrastructure.Options;

public sealed class MqttTowerOptions
{
    public const string SectionName = "MQTTower";

    public string BrokerHost { get; set; } = "127.0.0.1";
    public int BrokerPort { get; set; } = 1883;
    public string? BrokerUsername { get; set; }
    public string? BrokerPassword { get; set; }
    public string ControlTopic { get; set; } = "$CONTROL/dynamic-security/v1";
    public string DatabasePath { get; set; } = "Data Source=mqttower.db";
    public string MosquittoConfigPath { get; set; } = "/etc/mosquitto/mosquitto.conf";
    public string MosquittoLogPath { get; set; } = "/var/log/mosquitto/mosquitto.log";
    public int MetricsRetentionDays { get; set; } = 30;
    public string? NtfyBaseUrl { get; set; }
    public string? NtfyTopic { get; set; }

    /// <summary>Webhook URL for JSON POST notifications.</summary>
    public string? WebhookUrl { get; set; }

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpFrom { get; set; }
    public string? SmtpTo { get; set; }
}
