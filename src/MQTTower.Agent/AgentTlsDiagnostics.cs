using Microsoft.Extensions.Logging;

namespace MQTTower.Agent;

/// <summary>Kestrel HTTPS callbacks run before the host logger pipeline is available for injection; use a dedicated logger.</summary>
internal static class AgentTlsDiagnostics
{
    internal static readonly ILoggerFactory LogFactory = LoggerFactory.Create(b => b.AddConsole());
    internal static readonly ILogger TlsLogger = LogFactory.CreateLogger("MQTTower.Agent.Tls");
}
