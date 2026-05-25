using AiOps.McpServer.Models;

namespace AiOps.McpServer.Repositories;

public interface ILogRepository
{
    string Name { get; }
    string RepositoryType { get; }

    Task<IReadOnlyList<LogEntry>> QueryErrorsAsync(
        LogQueryOptions options,
        CancellationToken cancellationToken = default);
}
