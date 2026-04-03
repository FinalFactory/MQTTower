namespace MQTTower.Infrastructure.Options;

public sealed class MqttTowerOptions
{
    public const string SectionName = "MQTTower";

    /// <summary>MQTT broker host for the <b>dashboard</b> in-process client (local broker profile). Ignored by MQTTower.Agent (agent always uses localhost; port from mosquitto.conf).</summary>
    public string BrokerHost { get; set; } = "127.0.0.1";

    /// <summary>MQTT broker port for the <b>dashboard</b> in-process client (local broker profile). Ignored by MQTTower.Agent (port parsed from <see cref="MosquittoConfigPath"/>).</summary>
    public int BrokerPort { get; set; } = 1883;
    public string? BrokerUsername { get; set; }
    public string? BrokerPassword { get; set; }
    public string ControlTopic { get; set; } = "$CONTROL/dynamic-security/v1";
    public string DatabasePath { get; set; } = "Data Source=mqttower.db";
    public string MosquittoConfigPath { get; set; } = "/etc/mosquitto/mosquitto.conf";
    public string MosquittoLogPath { get; set; } = "/var/log/mosquitto/mosquitto.log";
    public int MetricsRetentionDays { get; set; } = 30;

    /// <summary>Maximum topic tree nodes in topic explorer; additional topics are ignored until cleared.</summary>
    public int TopicExplorerMaxNodes { get; set; } = 10_000;

    /// <summary>Audit log entries older than this are pruned when metrics collection runs.</summary>
    public int AuditRetentionDays { get; set; } = 90;
    public string? NtfyBaseUrl { get; set; }
    public string? NtfyTopic { get; set; }

    /// <summary>Webhook URL for JSON POST notifications.</summary>
    public string? WebhookUrl { get; set; }

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpFrom { get; set; }
    public string? SmtpTo { get; set; }

    /// <summary>Shared secret agents must send when calling <c>/api/agents/register</c>.</summary>
    public string? RegistrationSecret { get; set; }

    /// <summary>Optional client certificate path for mTLS to agents.</summary>
    public string? AgentClientCertPath { get; set; }

    /// <summary>Optional client certificate password.</summary>
    public string? AgentClientCertPassword { get; set; }

    /// <summary>Optional PEM/DER of a CA used to validate the agent server certificate when the broker TLS thumbprint is not pinned.</summary>
    public string? AgentTlsServerCaCertPath { get; set; }
}
