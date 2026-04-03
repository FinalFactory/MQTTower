namespace MQTTower.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string ApiKey { get; set; } = string.Empty;
    public string? DashboardUrl { get; set; }
    public string? RegistrationToken { get; set; }
    public bool AutoRegister { get; set; } = true;
    /// <summary>Optional shell command to restart Mosquitto (e.g. <c>docker kill -s HUP mosquitto</c>).</summary>
    public string? RestartCommand { get; set; }
    /// <summary>When set with HTTPS, validate dashboard client certificates against this CA (PEM/DER).</summary>
    public string? TlsCaCertPath { get; set; }

    /// <summary>Require TLS client certificate from callers (mTLS). Use with <see cref="TlsCaCertPath"/>.</summary>
    public bool RequireClientCertificate { get; set; }
    public string? ListenHttpsUrl { get; set; }
    /// <summary>Optional plain HTTP port (e.g. Docker dev). Set <see cref="HttpsPort"/> to 0 to disable TLS.</summary>
    public int HttpPort { get; set; }
    public string? CertificatePath { get; set; }
    public string? CertificateKeyPath { get; set; }
    /// <summary>PFX password when using bundled PFX.</summary>
    public string? CertificatePassword { get; set; }
    /// <summary>Public base URL of this agent (for registration). Defaults to listen URL if unset.</summary>
    public string? PublicAgentUrl { get; set; }
}
