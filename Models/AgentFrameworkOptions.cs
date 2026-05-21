namespace MultiAgentOrchestration.Blazor.Models;

public sealed class AgentFrameworkOptions
{
    public const string SectionName = "AgentFramework";

    public string ApiKey { get; set; } = string.Empty;

    public string Endpoint { get; set; } = "http://localhost:11434/v1";

    public string Model { get; set; } = "gpt-4o-mini";
}
