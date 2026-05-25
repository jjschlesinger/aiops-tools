using AiOps.McpServer.Configuration;
using AiOps.McpServer.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiOps.McpServer.Extensions;

public static class LogRepositoryServiceExtensions
{
    /// <summary>
    /// Reads <c>LogRepositories:Repositories</c> from configuration and registers each entry
    /// as an <see cref="ILogRepository"/> singleton. The concrete type is determined by the
    /// <see cref="LogRepositoryConfig.Type"/> field.
    /// </summary>
    public static IServiceCollection AddLogRepositories(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration
            .GetSection("LogRepositories")
            .Get<LogRepositorySettings>() ?? new LogRepositorySettings();

        foreach (var (name, config) in settings.Repositories)
        {
            ILogRepository repository = config.Type switch
            {
                "SerilogFile"  => new SerilogFileLogRepository(name, config),
                "Sql"          => new SqlLogRepository(name, config),
                "AzureMonitor" => new AzureMonitorLogRepository(name, config),
                _ => throw new InvalidOperationException(
                    $"Unknown repository type '{config.Type}' for repository '{name}'. " +
                    $"Supported types: SerilogFile, Sql, AzureMonitor.")
            };

            services.AddSingleton<ILogRepository>(repository);
        }

        return services;
    }
}
