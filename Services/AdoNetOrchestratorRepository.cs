using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgentOrchestration.Blazor.Models;
using System.Data;
using System.Text.RegularExpressions;

namespace MultiAgentOrchestration.Blazor.Services;

public sealed class AdoNetOrchestratorRepository : IOrchestratorRepository
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_\\[\\]\\.]+$", RegexOptions.Compiled);
    private readonly string _connectionString;
    private readonly string _configTable;
    private readonly string _mappingTable;
    private readonly ILogger<AdoNetOrchestratorRepository> _logger;

    public AdoNetOrchestratorRepository(
        IConfiguration configuration,
        IOptions<OrchestratorRepositoryOptions> options,
        ILogger<AdoNetOrchestratorRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        _configTable = options.Value.ConfigTableName;
        _mappingTable = options.Value.MappingTableName;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OrchestratorDefinition>> GetAllOrchestratorsAsync(bool activeOnly, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var configTable = GetSafeTableName(_configTable, nameof(OrchestratorRepositoryOptions.ConfigTableName));
        var sql = $@"
SELECT ID, ORCHESTRATOR_ID, NAME, DESCRIPTION, INTENT_KEYWORDS, IS_ACTIVE, SORT_ORDER, NO_MATCH_POLICY
FROM {configTable}
{(activeOnly ? "WHERE IS_ACTIVE = 1" : string.Empty)}
ORDER BY SORT_ORDER, ID";

        var result = new List<OrchestratorDefinition>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OrchestratorDefinition(
                RowId: reader["ID"] is DBNull ? null : Convert.ToInt32(reader["ID"]),
                OrchestratorId: Convert.ToString(reader["ORCHESTRATOR_ID"]) ?? string.Empty,
                Name: Convert.ToString(reader["NAME"]) ?? string.Empty,
                Description: reader["DESCRIPTION"] is DBNull ? null : Convert.ToString(reader["DESCRIPTION"]),
                IntentKeywords: reader["INTENT_KEYWORDS"] is DBNull ? null : Convert.ToString(reader["INTENT_KEYWORDS"]),
                IsActive: reader["IS_ACTIVE"] is not DBNull && Convert.ToInt32(reader["IS_ACTIVE"]) == 1,
                SortOrder: reader["SORT_ORDER"] is DBNull ? 0 : Convert.ToInt32(reader["SORT_ORDER"]),
                NoMatchPolicy: reader["NO_MATCH_POLICY"] is DBNull ? "fail" : Convert.ToString(reader["NO_MATCH_POLICY"]) ?? "fail"));
        }

        _logger.LogInformation("Loaded {Count} orchestrators from {Table}. ActiveOnly={ActiveOnly}", result.Count, configTable, activeOnly);
        return result;
    }

    public async Task<OrchestratorDefinition?> GetOrchestratorByIdAsync(string orchestratorId, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var configTable = GetSafeTableName(_configTable, nameof(OrchestratorRepositoryOptions.ConfigTableName));
        var sql = $@"
SELECT TOP 1 ID, ORCHESTRATOR_ID, NAME, DESCRIPTION, INTENT_KEYWORDS, IS_ACTIVE, SORT_ORDER, NO_MATCH_POLICY
FROM {configTable}
WHERE ORCHESTRATOR_ID = @orchestratorId";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@orchestratorId", SqlDbType.NVarChar, 100) { Value = orchestratorId });
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OrchestratorDefinition(
            RowId: reader["ID"] is DBNull ? null : Convert.ToInt32(reader["ID"]),
            OrchestratorId: Convert.ToString(reader["ORCHESTRATOR_ID"]) ?? string.Empty,
            Name: Convert.ToString(reader["NAME"]) ?? string.Empty,
            Description: reader["DESCRIPTION"] is DBNull ? null : Convert.ToString(reader["DESCRIPTION"]),
            IntentKeywords: reader["INTENT_KEYWORDS"] is DBNull ? null : Convert.ToString(reader["INTENT_KEYWORDS"]),
            IsActive: reader["IS_ACTIVE"] is not DBNull && Convert.ToInt32(reader["IS_ACTIVE"]) == 1,
            SortOrder: reader["SORT_ORDER"] is DBNull ? 0 : Convert.ToInt32(reader["SORT_ORDER"]),
            NoMatchPolicy: reader["NO_MATCH_POLICY"] is DBNull ? "fail" : Convert.ToString(reader["NO_MATCH_POLICY"]) ?? "fail");
    }

    public async Task CreateOrchestratorAsync(OrchestratorDefinition orchestrator, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var configTable = GetSafeTableName(_configTable, nameof(OrchestratorRepositoryOptions.ConfigTableName));
        var sql = $@"
INSERT INTO {configTable}
(ORCHESTRATOR_ID, NAME, DESCRIPTION, INTENT_KEYWORDS, IS_ACTIVE, SORT_ORDER, NO_MATCH_POLICY, CREATED_AT, UPDATED_AT)
VALUES
(@id, @name, @description, @keywords, @isActive, @sortOrder, @policy, SYSUTCDATETIME(), SYSUTCDATETIME())";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        AddConfigParameters(cmd, orchestrator);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateOrchestratorAsync(OrchestratorDefinition orchestrator, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var configTable = GetSafeTableName(_configTable, nameof(OrchestratorRepositoryOptions.ConfigTableName));
        var sql = $@"
UPDATE {configTable}
SET NAME = @name,
    DESCRIPTION = @description,
    INTENT_KEYWORDS = @keywords,
    IS_ACTIVE = @isActive,
    SORT_ORDER = @sortOrder,
    NO_MATCH_POLICY = @policy,
    UPDATED_AT = SYSUTCDATETIME()
WHERE ORCHESTRATOR_ID = @id";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        AddConfigParameters(cmd, orchestrator);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAgentMappingsAsync(string orchestratorId, IReadOnlyList<OrchestratorAgentMapping> mappings, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var mappingTable = GetSafeTableName(_mappingTable, nameof(OrchestratorRepositoryOptions.MappingTableName));
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var deleteSql = $"DELETE FROM {mappingTable} WHERE ORCHESTRATOR_ID = @orchestratorId";
            await using (var deleteCmd = new SqlCommand(deleteSql, conn, (SqlTransaction)tx))
            {
                deleteCmd.Parameters.Add(new SqlParameter("@orchestratorId", SqlDbType.NVarChar, 100) { Value = orchestratorId });
                await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var insertSql = $@"
INSERT INTO {mappingTable}
(ORCHESTRATOR_ID, AGENT_ID, DOMAIN_HINT, PRIORITY, IS_ACTIVE, CREATED_AT, UPDATED_AT)
VALUES
(@orchestratorId, @agentId, @domainHint, @priority, @isActive, SYSUTCDATETIME(), SYSUTCDATETIME())";

            foreach (var mapping in mappings)
            {
                await using var insertCmd = new SqlCommand(insertSql, conn, (SqlTransaction)tx);
                insertCmd.Parameters.Add(new SqlParameter("@orchestratorId", SqlDbType.NVarChar, 100) { Value = orchestratorId });
                insertCmd.Parameters.Add(new SqlParameter("@agentId", SqlDbType.NVarChar, 100) { Value = mapping.AgentId });
                insertCmd.Parameters.Add(new SqlParameter("@domainHint", SqlDbType.NVarChar, 400) { Value = (object?)mapping.DomainHint ?? DBNull.Value });
                insertCmd.Parameters.Add(new SqlParameter("@priority", SqlDbType.Int) { Value = mapping.Priority });
                insertCmd.Parameters.Add(new SqlParameter("@isActive", SqlDbType.Bit) { Value = mapping.IsActive });
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrchestratorAgentMapping>> GetMappedAgentsAsync(string orchestratorId, bool activeOnly, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var mappingTable = GetSafeTableName(_mappingTable, nameof(OrchestratorRepositoryOptions.MappingTableName));
        var sql = $@"
SELECT ORCHESTRATOR_ID, AGENT_ID, DOMAIN_HINT, PRIORITY, IS_ACTIVE
FROM {mappingTable}
WHERE ORCHESTRATOR_ID = @orchestratorId
{(activeOnly ? "AND IS_ACTIVE = 1" : string.Empty)}
ORDER BY PRIORITY, ID";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@orchestratorId", SqlDbType.NVarChar, 100) { Value = orchestratorId });
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var result = new List<OrchestratorAgentMapping>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OrchestratorAgentMapping(
                OrchestratorId: Convert.ToString(reader["ORCHESTRATOR_ID"]) ?? string.Empty,
                AgentId: Convert.ToString(reader["AGENT_ID"]) ?? string.Empty,
                DomainHint: reader["DOMAIN_HINT"] is DBNull ? null : Convert.ToString(reader["DOMAIN_HINT"]),
                Priority: reader["PRIORITY"] is DBNull ? 0 : Convert.ToInt32(reader["PRIORITY"]),
                IsActive: reader["IS_ACTIVE"] is not DBNull && Convert.ToBoolean(reader["IS_ACTIVE"])));
        }

        return result;
    }

    private void AddConfigParameters(SqlCommand cmd, OrchestratorDefinition orchestrator)
    {
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 100) { Value = orchestrator.OrchestratorId });
        cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 200) { Value = orchestrator.Name });
        cmd.Parameters.Add(new SqlParameter("@description", SqlDbType.NVarChar, -1) { Value = (object?)orchestrator.Description ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@keywords", SqlDbType.NVarChar, -1) { Value = (object?)orchestrator.IntentKeywords ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@isActive", SqlDbType.Bit) { Value = orchestrator.IsActive });
        cmd.Parameters.Add(new SqlParameter("@sortOrder", SqlDbType.Int) { Value = orchestrator.SortOrder });
        cmd.Parameters.Add(new SqlParameter("@policy", SqlDbType.NVarChar, 20) { Value = string.IsNullOrWhiteSpace(orchestrator.NoMatchPolicy) ? "fail" : orchestrator.NoMatchPolicy });
    }

    private void EnsureReady()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
        }
    }

    private static string GetSafeTableName(string tableName, string optionName)
    {
        if (!SafeIdentifier.IsMatch(tableName))
        {
            throw new InvalidOperationException($"{OrchestratorRepositoryOptions.SectionName}:{optionName} contains invalid characters.");
        }

        return tableName;
    }
}
