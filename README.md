# MultiAgentWorkFlow

A Blazor-based multi-agent orchestration system built on Microsoft Agent Framework concepts.

## What this project does
- Loads agent definitions from SQL (c.AGENT_CONFIG) with ADO.NET.
- Loads orchestrator definitions and agent mappings from SQL (c.ORCHESTRATOR_CONFIG, c.ORCHESTRATOR_AGENT_MAP).
- Routes user prompts through a selected orchestrator.
- Selects mapped active agents by intent/domain policy.
- Executes agents in deterministic order based on mapping priority.
- Streams live agent status and text in the UI.

## Key features implemented
- Orchestrator management UI (/orchestrator-config)
- Agent mapping per orchestrator
- Runtime orchestration page (/orchestration)
- Active-agent filtering and no-match fail policy
- SQL scripts for orchestrator schema in Sql/

## Tech stack
- .NET (Blazor Server)
- ADO.NET (Microsoft.Data.SqlClient)
- Microsoft Agents AI / Workflows packages

## Configuration
Set these in ppsettings.json:
- ConnectionStrings:DefaultConnection
- AgentFramework:Endpoint
- AgentFramework:Model
- AgentFramework:ApiKey (optional for Ollama)
- AgentRepository:TableName
- OrchestratorRepository:ConfigTableName
- OrchestratorRepository:MappingTableName

## Run locally
`ash
dotnet restore
dotnet run
`

## Database bootstrap
Run:
- Sql/orchestrator-schema.sql

## Notes
- Keep secrets (DB password/API keys) out of committed config for production.
- Prefer environment variables or user secrets for sensitive values.
