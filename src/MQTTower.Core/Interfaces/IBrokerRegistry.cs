using MQTTower.Core.Models;

namespace MQTTower.Core.Interfaces;

public interface IBrokerRegistry
{
    Task<IReadOnlyList<BrokerProfile>> ListAsync(CancellationToken cancellationToken = default);
    Task<BrokerProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BrokerProfile?> GetByAgentUrlAsync(string agentUrl, CancellationToken cancellationToken = default);
    Task<BrokerProfile?> GetDefaultLocalAsync(CancellationToken cancellationToken = default);
    Task<BrokerProfile> AddAsync(BrokerProfile profile, CancellationToken cancellationToken = default);
    Task UpdateAsync(BrokerProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
