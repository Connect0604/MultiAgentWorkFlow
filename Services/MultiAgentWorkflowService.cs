using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgentOrchestration.Blazor.Models;
using OpenAI;
using System.ClientModel;
using System.Text.RegularExpressions;

namespace MultiAgentOrchestration.Blazor.Services;

public sealed class MultiAgentWorkflowService
{
    private static readonly Regex RuntimeSuffixRegex = new("^(?<base>.+)_(?<suffix>[0-9a-f]{16,})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly AgentFrameworkOptions _options;
    private readonly AgentRepositoryOptions _repositoryOptions;
    private readonly DefaultAgentsOptions _defaultAgentsOptions;
    private readonly IAgentDefinitionRepository _agentRepository;
    private readonly IOrchestratorRepository _orchestratorRepository;
    private readonly IntentRoutingService _routingService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<MultiAgentWorkflowService> _logger;

    public MultiAgentWorkflowService(
        IOptions<AgentFrameworkOptions> options,
        IOptions<AgentRepositoryOptions> repositoryOptions,
        IOptions<DefaultAgentsOptions> defaultAgentsOptions,
        IAgentDefinitionRepository agentRepository,
        IOrchestratorRepository orchestratorRepository,
        IntentRoutingService routingService,
        IWebHostEnvironment environment,
        ILogger<MultiAgentWorkflowService> logger)
    {
        _options = options.Value;
        _repositoryOptions = repositoryOptions.Value;
        _defaultAgentsOptions = defaultAgentsOptions.Value;
        _agentRepository = agentRepository;
        _orchestratorRepository = orchestratorRepository;
        _routingService = routingService;
        _environment = environment;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AgentDefinition>> GetAllAgentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var all = await _agentRepository.GetAllAsync(cancellationToken);
            if (all.Count > 0)
            {
                return all;
            }

            if (CanFallback())
            {
                _logger.LogWarning("No agents found in DB. Falling back to in-memory defaults.");
                return GetDefaultAgents();
            }

            throw new InvalidOperationException("No agent definitions found in DB and fallback is disabled.");
        }
        catch (Exception ex) when (CanFallback())
        {
            _logger.LogError(ex, "Failed to load all agents from DB. Falling back to in-memory defaults.");
            return GetDefaultAgents();
        }
    }

    public async Task UpdateAgentInstructionsAsync(string agentId, string instructions, CancellationToken cancellationToken = default)
    {
        await _agentRepository.UpdateInstructionsAsync(agentId, instructions, cancellationToken);
    }

    public IReadOnlyList<AgentDefinition> GetDefaultAgents()
    {
        return _defaultAgentsOptions.Agents
            .Where(a => a.IsEnabled)
            .Select(a => new AgentDefinition(
                RowId: null,
                Id: a.Id,
                Title: a.Title,
                Instructions: a.Instructions,
                Domain: a.Domain,
                Description: a.Description,
                IsActive: true,
                SortOrder: a.SortOrder))
            .ToList();
    }

    public async Task<OrchestrationResult> RunConcurrentAsync(
        string prompt,
        string workflowMode = "auto",
        Action<WorkflowExecutionPlan>? onWorkflowPlanned = null,
        Action<string, string>? onAgentStatusChanged = null,
        Action<string, string>? onAgentTextChunk = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("AgentFramework:Model is not configured.");
        }

        var definitions = await GetActiveAgentsOrFallbackAsync(cancellationToken);
        if (definitions.Count == 0)
        {
            throw new InvalidOperationException("No agents are available for orchestration. Activate agents in DB or enable at least one default agent.");
        }

        var plan = _routingService.BuildPlan(prompt, workflowMode, definitions);
        onWorkflowPlanned?.Invoke(plan);

        var selectedDefinitions = plan.SelectedAgentIds
            .Select(id => definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        if (selectedDefinitions.Count == 0)
        {
            throw new InvalidOperationException("No eligible agents matched the current workflow intent. Adjust agent domains or workflow selection.");
        }

        return await ExecuteWithDefinitionsAsync(prompt, selectedDefinitions, onAgentStatusChanged, onAgentTextChunk, cancellationToken);
    }

    public async Task<IReadOnlyList<OrchestratorDefinition>> GetActiveOrchestratorsAsync(CancellationToken cancellationToken = default)
    {
        return await _orchestratorRepository.GetAllOrchestratorsAsync(activeOnly: true, cancellationToken);
    }

    public async Task<IReadOnlyList<OrchestratorDefinition>> GetAllOrchestratorsAsync(CancellationToken cancellationToken = default)
    {
        return await _orchestratorRepository.GetAllOrchestratorsAsync(activeOnly: false, cancellationToken);
    }

    public async Task SaveOrchestratorAsync(OrchestratorDefinition orchestrator, IReadOnlyList<OrchestratorAgentMapping> mappings, CancellationToken cancellationToken = default)
    {
        var existing = await _orchestratorRepository.GetOrchestratorByIdAsync(orchestrator.OrchestratorId, cancellationToken);
        if (existing is null)
        {
            await _orchestratorRepository.CreateOrchestratorAsync(orchestrator, cancellationToken);
        }
        else
        {
            await _orchestratorRepository.UpdateOrchestratorAsync(orchestrator, cancellationToken);
        }

        await _orchestratorRepository.SaveAgentMappingsAsync(orchestrator.OrchestratorId, mappings, cancellationToken);
    }

    public async Task<IReadOnlyList<OrchestratorAgentMapping>> GetOrchestratorMappingsAsync(string orchestratorId, bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        return await _orchestratorRepository.GetMappedAgentsAsync(orchestratorId, activeOnly, cancellationToken);
    }

    public async Task<OrchestrationResult> RunOrchestratorAsync(
        OrchestratorExecutionRequest request,
        Action<WorkflowExecutionPlan>? onWorkflowPlanned = null,
        Action<string, string>? onAgentStatusChanged = null,
        Action<string, string>? onAgentTextChunk = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OrchestratorId))
        {
            throw new InvalidOperationException("OrchestratorId is required.");
        }

        var orchestrator = await _orchestratorRepository.GetOrchestratorByIdAsync(request.OrchestratorId, cancellationToken);
        if (orchestrator is null)
        {
            throw new InvalidOperationException($"Orchestrator '{request.OrchestratorId}' was not found.");
        }

        if (!orchestrator.IsActive)
        {
            throw new InvalidOperationException($"Orchestrator '{request.OrchestratorId}' is inactive and cannot execute.");
        }

        var mappings = await _orchestratorRepository.GetMappedAgentsAsync(request.OrchestratorId, activeOnly: true, cancellationToken);
        if (mappings.Count == 0)
        {
            throw new InvalidOperationException($"No active mapped agents are configured for orchestrator '{request.OrchestratorId}'.");
        }

        var activeAgents = await _agentRepository.GetActiveAsync(cancellationToken);
        var mappedAgents = activeAgents
            .Where(a => mappings.Any(m => string.Equals(m.AgentId, a.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (mappedAgents.Count == 0)
        {
            throw new InvalidOperationException($"Mapped agents are configured for orchestrator '{request.OrchestratorId}', but none are active in agent configuration.");
        }

        var plan = _routingService.BuildPlanForOrchestrator(request.Prompt, orchestrator, mappedAgents, mappings);
        if (plan.SelectedAgentIds.Count == 0)
        {
            throw new InvalidOperationException($"No eligible mapped agents found for orchestrator '{request.OrchestratorId}'.");
        }

        onWorkflowPlanned?.Invoke(plan);
        var selectedDefinitions = plan.SelectedAgentIds
            .Select(id => mappedAgents.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        return await ExecuteWithDefinitionsAsync(request.Prompt, selectedDefinitions, onAgentStatusChanged, onAgentTextChunk, cancellationToken);
    }

    private async Task<OrchestrationResult> ExecuteWithDefinitionsAsync(
        string prompt,
        IReadOnlyList<AgentDefinition> selectedDefinitions,
        Action<string, string>? onAgentStatusChanged,
        Action<string, string>? onAgentTextChunk,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("AgentFramework:Model is not configured.");
        }

        var endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
            ? "http://localhost:11434/v1"
            : _options.Endpoint;
        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey) ? "ollama" : _options.ApiKey;

        var nativeClient = new OpenAI.Chat.ChatClient(
            _options.Model,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        var chatClient = nativeClient.AsIChatClient();

        var updates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var finalMessages = new List<string>();
        foreach (var agent in selectedDefinitions)
        {
            onAgentStatusChanged?.Invoke(agent.Id, "Queued");
        }

        foreach (var definition in selectedDefinitions)
        {
            var runId = Guid.NewGuid().ToString("N");
            var runtimeAgentId = $"{definition.Id}_{runId}";
            var normalizedId = definition.Id;
            var singleAgent = CreateAgent(runtimeAgentId, definition.Instructions, chatClient);
            var workflow = AgentWorkflowBuilder.BuildConcurrent([singleAgent]);
            var input = new List<ChatMessage> { new(ChatRole.User, prompt) };

            await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
                workflow,
                input,
                cancellationToken: cancellationToken);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken))
            {
                switch (evt)
                {
                    case AgentResponseUpdateEvent updateEvent:
                        onAgentStatusChanged?.Invoke(normalizedId, "Working");
                        if (!string.IsNullOrWhiteSpace(updateEvent.Update.Text))
                        {
                            onAgentTextChunk?.Invoke(normalizedId, updateEvent.Update.Text);

                            if (!updates.TryGetValue(normalizedId, out var chunks))
                            {
                                chunks = new List<string>();
                                updates[normalizedId] = chunks;
                            }

                            chunks.Add(updateEvent.Update.Text);
                        }
                        break;
                    case WorkflowOutputEvent outputEvent:
                        var output = outputEvent.As<List<ChatMessage>>();
                        if (output is not null)
                        {
                            onAgentStatusChanged?.Invoke(normalizedId, "Completed");
                            finalMessages.AddRange(output
                                .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                                .Select(m => m.Text!.Trim()));
                        }
                        break;
                }
            }

            // Guard against late/out-of-order stream updates leaving agent in Working state.
            onAgentStatusChanged?.Invoke(normalizedId, "Completed");
        }

        var outputs = updates
            .Select(x => new AgentOutput(x.Key, string.Concat(x.Value).Trim()))
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OrchestrationResult(outputs, finalMessages);
    }

    private async Task<IReadOnlyList<AgentDefinition>> GetActiveAgentsOrFallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            var active = await _agentRepository.GetActiveAsync(cancellationToken);
            if (active.Count > 0)
            {
                return active;
            }

            if (CanFallback())
            {
                _logger.LogWarning("No active agents found in DB. Falling back to in-memory defaults.");
                return GetDefaultAgents();
            }

            throw new InvalidOperationException("No active agents found in DB and fallback is disabled.");
        }
        catch (Exception ex) when (CanFallback())
        {
            _logger.LogError(ex, "Failed to load active agents from DB. Falling back to in-memory defaults.");
            return GetDefaultAgents();
        }
    }

    private bool CanFallback()
    {
        return _repositoryOptions.AllowFallbackToDefaults || _environment.IsDevelopment();
    }

    private static ChatClientAgent CreateAgent(string id, string instructions, IChatClient chatClient)
    {
        return new ChatClientAgent(chatClient, instructions, id);
    }

    private static string NormalizeAgentId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return rawId;
        }

        var runtimeMatch = RuntimeSuffixRegex.Match(rawId);
        if (runtimeMatch.Success)
        {
            return runtimeMatch.Groups["base"].Value;
        }

        var lastUnderscore = rawId.LastIndexOf('_');
        if (lastUnderscore > 0 && lastUnderscore < rawId.Length - 1)
        {
            var suffix = rawId[(lastUnderscore + 1)..];
            if (suffix.Length >= 16 && suffix.All(c => Uri.IsHexDigit(c)))
            {
                return rawId[..lastUnderscore];
            }
        }

        if (rawId.StartsWith("researcher", StringComparison.OrdinalIgnoreCase))
        {
            return "researcher";
        }

        if (rawId.StartsWith("architect", StringComparison.OrdinalIgnoreCase))
        {
            return "architect";
        }

        if (rawId.StartsWith("reviewer", StringComparison.OrdinalIgnoreCase))
        {
            return "reviewer";
        }

        return rawId;
    }
}
