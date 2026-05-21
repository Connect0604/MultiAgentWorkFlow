using MultiAgentOrchestration.Blazor.Models;

namespace MultiAgentOrchestration.Blazor.Services;

public sealed class IntentRoutingService
{
    public WorkflowExecutionPlan BuildPlan(string prompt, string workflowMode, IReadOnlyList<AgentDefinition> activeAgents)
    {
        var mode = string.IsNullOrWhiteSpace(workflowMode) ? "auto" : workflowMode.Trim().ToLowerInvariant();
        var normalizedPrompt = (prompt ?? string.Empty).ToLowerInvariant();
        var complexityIsHigh = IsHighComplexity(normalizedPrompt);

        var candidateDomains = mode switch
        {
            "plan-antipattern" => ["planning", "plan", "architecture", "antipattern"],
            "code-review-checklist" => ["code", "review", "quality", "checklist"],
            _ => DetectDomains(normalizedPrompt)
        };

        var selected = SelectAgentsByDomain(activeAgents, candidateDomains, complexityIsHigh);
        var workflowId = mode == "auto" ? InferWorkflowId(normalizedPrompt) : mode;
        var workflowName = ToWorkflowName(workflowId);
        var reason = mode == "auto"
            ? $"Auto intent matched domains: {string.Join(", ", candidateDomains)}"
            : $"Manual workflow mode selected: {workflowName}";

        return new WorkflowExecutionPlan(
            workflowId,
            workflowName,
            reason,
            selected.Select(a => a.Id).ToList());
    }

    private static List<string> DetectDomains(string normalizedPrompt)
    {
        if (normalizedPrompt.Contains("antipattern") || normalizedPrompt.Contains("plan.md") || normalizedPrompt.Contains("architecture"))
        {
            return ["planning", "plan", "architecture", "antipattern"];
        }

        if (normalizedPrompt.Contains("code review") || normalizedPrompt.Contains("checklist") || normalizedPrompt.Contains("pull request"))
        {
            return ["code", "review", "quality", "checklist"];
        }

        return ["general"];
    }

    private static string InferWorkflowId(string normalizedPrompt)
    {
        if (normalizedPrompt.Contains("antipattern") || normalizedPrompt.Contains("plan.md"))
        {
            return "plan-antipattern";
        }

        if (normalizedPrompt.Contains("code review") || normalizedPrompt.Contains("checklist"))
        {
            return "code-review-checklist";
        }

        return "general-assistant";
    }

    private static string ToWorkflowName(string workflowId)
    {
        return workflowId switch
        {
            "plan-antipattern" => "Plan Anti-Pattern Analysis",
            "code-review-checklist" => "Code Review Checklist",
            _ => "General Assistant Workflow"
        };
    }

    private static List<AgentDefinition> SelectAgentsByDomain(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<string> domains,
        bool complexityIsHigh)
    {
        var matched = agents
            .Where(a => !string.IsNullOrWhiteSpace(a.Domain) &&
                        domains.Any(d => a.Domain!.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.SortOrder)
            .ToList();

        if (matched.Count == 0)
        {
            matched = agents.OrderBy(a => a.SortOrder).ToList();
        }

        if (complexityIsHigh)
        {
            return matched;
        }

        return matched.Take(Math.Min(2, matched.Count)).ToList();
    }

    private static bool IsHighComplexity(string normalizedPrompt)
    {
        return normalizedPrompt.Length > 350
               || normalizedPrompt.Contains("deep")
               || normalizedPrompt.Contains("thorough")
               || normalizedPrompt.Contains("comprehensive")
               || normalizedPrompt.Contains("end-to-end");
    }

    public WorkflowExecutionPlan BuildPlanForOrchestrator(
        string prompt,
        OrchestratorDefinition orchestrator,
        IReadOnlyList<AgentDefinition> mappedAgents,
        IReadOnlyList<OrchestratorAgentMapping> mappings)
    {
        var normalizedPrompt = (prompt ?? string.Empty).ToLowerInvariant();
        var complexityIsHigh = IsHighComplexity(normalizedPrompt);
        var candidateDomains = DetectDomains(normalizedPrompt);
        var isGeneralIntent = candidateDomains.Count == 1
            && string.Equals(candidateDomains[0], "general", StringComparison.OrdinalIgnoreCase);

        var scored = mappedAgents
            .Select(a =>
            {
                var mapping = mappings.FirstOrDefault(m => string.Equals(m.AgentId, a.Id, StringComparison.OrdinalIgnoreCase));
                var domain = (mapping?.DomainHint ?? a.Domain ?? string.Empty).ToLowerInvariant();
                var score = candidateDomains.Count(d => domain.Contains(d, StringComparison.OrdinalIgnoreCase));
                return new { Agent = a, Mapping = mapping, Score = score };
            })
            .Where(x => x.Mapping is not null)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Mapping!.Priority)
            .ThenBy(x => x.Agent.SortOrder)
            .ToList();

        var eligible = isGeneralIntent
            ? scored
            : scored.Where(x => x.Score > 0).ToList();
        if (eligible.Count == 0)
        {
            throw new InvalidOperationException(
                $"No eligible mapped agents matched intent for orchestrator '{orchestrator.OrchestratorId}'. Update domains/keywords or mappings.");
        }

        var takeCount = (complexityIsHigh || isGeneralIntent) ? eligible.Count : Math.Min(2, eligible.Count);
        var selected = eligible.Take(takeCount).Select(x => x.Agent.Id).ToList();
        var reason = $"Intent domains: {string.Join(", ", candidateDomains)}";

        return new WorkflowExecutionPlan(
            WorkflowId: orchestrator.OrchestratorId,
            WorkflowName: orchestrator.Name,
            Reason: reason,
            SelectedAgentIds: selected,
            SelectedOrchestratorId: orchestrator.OrchestratorId,
            SelectedOrchestratorName: orchestrator.Name,
            MappingPoolCount: mappedAgents.Count);
    }
}
