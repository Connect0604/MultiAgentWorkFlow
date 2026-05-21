using System.Text.Json;
using System.Text.Json.Nodes;
using MultiAgentOrchestration.Blazor.Models;

namespace MultiAgentOrchestration.Blazor.Services;

public sealed class AppSettingsService
{
    private readonly string _appSettingsPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AppSettingsService(IWebHostEnvironment environment)
    {
        _appSettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");
    }

    public async Task<AgentFrameworkOptions> GetAgentFrameworkSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadRootAsync(cancellationToken);
            var section = root["AgentFramework"] as JsonObject;
            if (section is null)
            {
                return new AgentFrameworkOptions();
            }

            return new AgentFrameworkOptions
            {
                ApiKey = section["ApiKey"]?.GetValue<string>() ?? string.Empty,
                Endpoint = section["Endpoint"]?.GetValue<string>() ?? "http://localhost:11434/v1",
                Model = section["Model"]?.GetValue<string>() ?? "llama3.1"
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAgentFrameworkSettingsAsync(AgentFrameworkOptions settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadRootAsync(cancellationToken);
            var section = (root["AgentFramework"] as JsonObject) ?? new JsonObject();
            section["ApiKey"] = settings.ApiKey ?? string.Empty;
            section["Endpoint"] = settings.Endpoint ?? "http://localhost:11434/v1";
            section["Model"] = settings.Model ?? "llama3.1";
            root["AgentFramework"] = section;

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_appSettingsPath, json, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<JsonObject> ReadRootAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_appSettingsPath))
        {
            return new JsonObject();
        }

        var raw = await File.ReadAllTextAsync(_appSettingsPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(raw)?.AsObject() ?? new JsonObject();
    }
}
