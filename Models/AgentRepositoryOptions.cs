namespace MultiAgentOrchestration.Blazor.Models;

public sealed class AgentRepositoryOptions
{
    public const string SectionName = "AgentRepository";

    public string TableName { get; set; } = "AGENT_DEFINITION";

    public bool AllowFallbackToDefaults { get; set; } = true;
}
