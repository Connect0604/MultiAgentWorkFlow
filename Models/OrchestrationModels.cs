namespace MultiAgentOrchestration.Blazor.Models;

public sealed record AgentOutput(string AgentId, string Text);

public sealed record AgentDefinition(
    int? RowId,
    string Id,
    string Title,
    string Instructions,
    string? Domain = null,
    string? Description = null,
    bool IsActive = true,
    int SortOrder = 0);

public sealed record OrchestrationResult(
    IReadOnlyList<AgentOutput> AgentOutputs,
    IReadOnlyList<string> FinalMessages);

public sealed record WorkflowExecutionPlan(
    string WorkflowId,
    string WorkflowName,
    string Reason,
    IReadOnlyList<string> SelectedAgentIds,
    string? SelectedOrchestratorId = null,
    string? SelectedOrchestratorName = null,
    int MappingPoolCount = 0);

public sealed record OrchestratorDefinition(
    int? RowId,
    string OrchestratorId,
    string Name,
    string? Description,
    string? IntentKeywords,
    bool IsActive,
    int SortOrder,
    string NoMatchPolicy = "fail");

public sealed record OrchestratorAgentMapping(
    string OrchestratorId,
    string AgentId,
    string? DomainHint,
    int Priority,
    bool IsActive);

public sealed record OrchestratorExecutionRequest(
    string OrchestratorId,
    string Prompt,
    string WorkflowMode = "orchestrator");
