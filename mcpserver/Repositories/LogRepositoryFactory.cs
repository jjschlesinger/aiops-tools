using AiOps.McpServer.Configuration;
using Microsoft.Extensions.Options;

namespace AiOps.McpServer.Repositories;

public sealed class LogRepositoryFactory(IOptionsMonitor<LogRepositorySettings> settingsMonitor)
    : ILogRepositoryFactory
{
    public ILogRepository GetRepository(string name)
    {
        var repos = settingsMonitor.CurrentValue.Repositories;

        if (!repos.TryGetValue(name, out var config))
            throw new InvalidOperationException(
                $"No log repository configured with name '{name}'. " +
                $"Available: {string.Join(", ", repos.Keys)}");

        return config.Type switch
        {
            "SerilogFile" => new SerilogFileLogRepository(name, config),
            "Sql" => new SqlLogRepository(name, config),
            "AzureMonitor" => new AzureMonitorLogRepository(name, config),
            _ => throw new InvalidOperationException(
                $"Unknown repository type '{config.Type}' for '{name}'. " +
                $"Supported types: SerilogFile, Sql, AzureMonitor")
        };
    }

    public IReadOnlyDictionary<string, string> GetAvailableRepositories() =>
        settingsMonitor.CurrentValue.Repositories
            .ToDictionary(kv => kv.Key, kv => kv.Value.Type, StringComparer.OrdinalIgnoreCase);
}
