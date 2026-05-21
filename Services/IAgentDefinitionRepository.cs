using MultiAgentOrchestration.Blazor.Models;

namespace MultiAgentOrchestration.Blazor.Services;

public interface IAgentDefinitionRepository
{
    Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentDefinition>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task UpdateInstructionsAsync(string agentId, string instructions, CancellationToken cancellationToken = default);
}
