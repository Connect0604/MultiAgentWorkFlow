namespace MultiAgentOrchestration.Blazor.Models;

public sealed class OrchestratorRepositoryOptions
{
    public const string SectionName = "OrchestratorRepository";

    public string ConfigTableName { get; set; } = "[ac].[ORCHESTRATOR_CONFIG]";

    public string MappingTableName { get; set; } = "[ac].[ORCHESTRATOR_AGENT_MAP]";
}
