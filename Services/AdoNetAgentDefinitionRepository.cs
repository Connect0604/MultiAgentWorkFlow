using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MultiAgentOrchestration.Blazor.Models;
using System.Text.RegularExpressions;

namespace MultiAgentOrchestration.Blazor.Services;

public sealed class AdoNetAgentDefinitionRepository : IAgentDefinitionRepository
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_\\[\\]\\.]+$", RegexOptions.Compiled);
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger<AdoNetAgentDefinitionRepository> _logger;

    public AdoNetAgentDefinitionRepository(
        IConfiguration configuration,
        IOptions<AgentRepositoryOptions> options,
        ILogger<AdoNetAgentDefinitionRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        _tableName = options.Value.TableName;
        _logger = logger;
    }

    public Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ReadAsync(includeOnlyActive: false, cancellationToken);
    }

    public Task<IReadOnlyList<AgentDefinition>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return ReadAsync(includeOnlyActive: true, cancellationToken);
    }

    public async Task UpdateInstructionsAsync(string agentId, string instructions, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var table = GetSafeTableName();
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = $"UPDATE {table} SET INSTRUCTIONS = @instructions WHERE AGENT_ID = @agentId";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@instructions", System.Data.SqlDbType.NVarChar, -1)
            {
                Value = instructions ?? string.Empty
            });
            cmd.Parameters.Add(new SqlParameter("@agentId", System.Data.SqlDbType.NVarChar, 100)
            {
                Value = agentId ?? string.Empty
            });
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update instructions for agent {AgentId} in table {Table}", agentId, table);
            throw;
        }
    }

    private async Task<IReadOnlyList<AgentDefinition>> ReadAsync(bool includeOnlyActive, CancellationToken cancellationToken)
    {
        EnsureReady();
        var table = GetSafeTableName();

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = $@"
SELECT ID, AGENT_ID, NAME, DOMAIN, DESCRIPTION, INSTRUCTIONS, IS_ACTIVE, SORT_ORDER
FROM {table}
{(includeOnlyActive ? "WHERE IS_ACTIVE = 1" : string.Empty)}
ORDER BY SORT_ORDER, ID";

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var result = new List<AgentDefinition>();
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new AgentDefinition(
                    RowId: reader["ID"] is DBNull ? null : Convert.ToInt32(reader["ID"]),
                    Id: Convert.ToString(reader["AGENT_ID"]) ?? string.Empty,
                    Title: Convert.ToString(reader["NAME"]) ?? string.Empty,
                    Instructions: Convert.ToString(reader["INSTRUCTIONS"]) ?? string.Empty,
                    Domain: reader["DOMAIN"] is DBNull ? null : Convert.ToString(reader["DOMAIN"]),
                    Description: reader["DESCRIPTION"] is DBNull ? null : Convert.ToString(reader["DESCRIPTION"]),
                    IsActive: reader["IS_ACTIVE"] is not DBNull && Convert.ToInt32(reader["IS_ACTIVE"]) == 1,
                    SortOrder: reader["SORT_ORDER"] is DBNull ? 0 : Convert.ToInt32(reader["SORT_ORDER"])));
            }

            _logger.LogInformation("Loaded {Count} agents from table {Table}. ActiveOnly={ActiveOnly}", result.Count, table, includeOnlyActive);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load agents from table {Table}. ActiveOnly={ActiveOnly}", table, includeOnlyActive);
            throw;
        }
    }

    private void EnsureReady()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
        }
    }

    private string GetSafeTableName()
    {
        if (!SafeIdentifier.IsMatch(_tableName))
        {
            throw new InvalidOperationException("AgentRepository:TableName contains invalid characters.");
        }

        return _tableName;
    }
}
