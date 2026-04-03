namespace MQTTower.Agent;

/// <summary>Limits concurrent SSE connections per stream endpoint to reduce resource exhaustion.</summary>
public sealed class AgentSseLimits
{
    public SemaphoreSlim Stats { get; } = new(5, 5);
    public SemaphoreSlim Logs { get; } = new(5, 5);
    public SemaphoreSlim Topics { get; } = new(5, 5);
}
