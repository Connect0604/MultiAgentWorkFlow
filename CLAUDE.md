# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
# Run the app (Kestrel + hot reload)
dotnet watch

# Build only
dotnet build

# Run without watch
dotnet run
```

There are no test projects in this repository.

## Architecture

Blazor Server app (`.NET 10`, Interactive Server render mode) that provides a web UI for configuring and running concurrent multi-agent AI workflows against an OpenAI-compatible endpoint (Ollama or any OpenAI API).

### Request flow

1. User submits a prompt on `/orchestration`, selecting an active orchestrator.
2. `MultiAgentWorkflowService.RunOrchestratorAsync` loads the orchestrator and its mapped agents from SQL Server via `IOrchestratorRepository`.
3. `IntentRoutingService.BuildPlanForOrchestrator` scores agents by domain match against the prompt and picks up to 2 (or all for "high-complexity" prompts >350 chars or containing `deep`/`thorough`/`comprehensive`/`end-to-end`).
4. `ExecuteWithDefinitionsAsync` builds `ChatClientAgent` instances and runs them **concurrently** via `AgentWorkflowBuilder.BuildConcurrent` from `Microsoft.Agents.AI.Workflows`.
5. Streaming events (`AgentResponseUpdateEvent`, `WorkflowOutputEvent`) flow back as Blazor state changes via `InvokeAsync(StateHasChanged)`.

### Service layer

| Service | DI lifetime | Responsibility |
|---|---|---|
| `MultiAgentWorkflowService` | Scoped | Orchestrates agent loading, plan building, and workflow execution |
| `IntentRoutingService` | Scoped | Keyword-based intent detection + agent selection scoring |
| `AdoNetAgentDefinitionRepository` | Scoped | Raw ADO.NET queries against `[ac].[AGENT_CONFIG]` |
| `AdoNetOrchestratorRepository` | Scoped | Raw ADO.NET queries against `[ac].[ORCHESTRATOR_CONFIG]` and `[ac].[ORCHESTRATOR_AGENT_MAP]` |
| `AppSettingsService` | Singleton | Reads/writes `appsettings.json` live; uses `SemaphoreSlim(1,1)` to serialize concurrent access |

Both repository implementations validate table names with a compiled regex (`^[A-Za-z0-9_\[\]\.]+$`) before interpolating into SQL to guard against injection via misconfigured options.

### Fallback to in-memory defaults

When SQL Server is unreachable or returns no rows, `MultiAgentWorkflowService` falls back to the `DefaultAgents` config section if `AgentRepository:AllowFallbackToDefaults` is `true` **or** the environment is `Development`. Default agents in `appsettings.json` are disabled (`IsEnabled: false`) by default and must be opted in.

### Configuration sections

```
AgentFramework:
  Endpoint   – OpenAI-compatible base URL (default: http://localhost:11434/v1)
  Model      – model name passed to OpenAI client
  ApiKey     – API key ("ollama" used as placeholder when blank)

ConnectionStrings:DefaultConnection – SQL Server connection string

AgentRepository:
  TableName               – fully-qualified table for agent config (default: [ac].[AGENT_CONFIG])
  AllowFallbackToDefaults – whether to fall back to in-memory agents on DB failure

OrchestratorRepository:
  ConfigTableName  – orchestrator config table
  MappingTableName – orchestrator-to-agent mapping table

DefaultAgents:Agents[] – in-memory fallback agent definitions (Id, Title, Instructions, Domain, IsEnabled, SortOrder)
```

`AppSettingsService` writes only the `AgentFramework` section back to `appsettings.json` at runtime when the user saves via the Settings page.

### Pages

| Route | Purpose |
|---|---|
| `/` | Home / landing |
| `/orchestration` | Run a workflow; select orchestrator, enter prompt, see live streaming output |
| `/agents` | List agents from DB; edit and save `INSTRUCTIONS` column in place |
| `/orchestrator-config` | CRUD for orchestrators; map active agents with domain hints and priority |
| `/settings` | Edit `AgentFramework` settings (endpoint, model, key) written to `appsettings.json` |
| `/tools` | Utility page |
| `/hooks` | Webhook/hook configuration page |

### Key types (`Models/OrchestrationModels.cs`)

- `AgentDefinition` – immutable record: Id, Title, Instructions, Domain, IsActive, SortOrder
- `OrchestratorDefinition` – orchestrator config with `NoMatchPolicy` ("fail" is the only implemented policy)
- `OrchestratorAgentMapping` – join between orchestrator and agent, carrying `DomainHint` and `Priority`
- `WorkflowExecutionPlan` – intent routing result; carries selected agent IDs and the reason for selection
- `OrchestrationResult` – final output: per-agent text chunks + aggregated final messages

### NormalizeAgentId

`MultiAgentWorkflowService.NormalizeAgentId` strips the suffix that `Microsoft.Agents.AI` appends to executor IDs (e.g., `researcher_abc123` → `researcher`). If you add new agent IDs, extend this method with a corresponding `StartsWith` branch.
