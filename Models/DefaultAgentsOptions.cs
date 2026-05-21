namespace MultiAgentOrchestration.Blazor.Models;

public sealed class DefaultAgentsOptions
{
    public const string SectionName = "DefaultAgents";

    public List<DefaultAgentItem> Agents { get; set; } = [];
}

public sealed class DefaultAgentItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
}
