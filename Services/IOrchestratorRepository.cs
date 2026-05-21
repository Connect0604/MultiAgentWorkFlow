using MultiAgentOrchestration.Blazor.Models;

namespace MultiAgentOrchestration.Blazor.Services;

public interface IOrchestratorRepository
{
    Task<IReadOnlyList<OrchestratorDefinition>> GetAllOrchestratorsAsync(bool activeOnly, CancellationToken cancellationToken = default);
    Task<OrchestratorDefinition?> GetOrchestratorByIdAsync(string orchestratorId, CancellationToken cancellationToken = default);
    Task CreateOrchestratorAsync(OrchestratorDefinition orchestrator, CancellationToken cancellationToken = default);
    Task UpdateOrchestratorAsync(OrchestratorDefinition orchestrator, CancellationToken cancellationToken = default);
    Task SaveAgentMappingsAsync(string orchestratorId, IReadOnlyList<OrchestratorAgentMapping> mappings, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrchestratorAgentMapping>> GetMappedAgentsAsync(string orchestratorId, bool activeOnly, CancellationToken cancellationToken = default);
}
