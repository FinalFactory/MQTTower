using Microsoft.Extensions.Options;

namespace MQTTower.Agent;

/// <summary>Runtime API key for the agent process (updated via <c>POST /api/agent/key</c>).</summary>
public sealed class AgentApiKeyState
{
    private string _key;

    public AgentApiKeyState(IOptions<AgentOptions> opts)
    {
        _key = opts.Value.ApiKey ?? string.Empty;
    }

    public string CurrentKey => _key;

    public void SetKey(string key) => _key = key ?? string.Empty;
}
