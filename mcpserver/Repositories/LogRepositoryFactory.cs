namespace AiOps.McpServer.Repositories;

public sealed class LogRepositoryFactory(IEnumerable<ILogRepository> repositories) : ILogRepositoryFactory
{
    private readonly IReadOnlyList<ILogRepository> _repositories = repositories.ToList();

    public ILogRepository GetRepository(string name) =>
        _repositories.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"No log repository registered with name '{name}'. " +
            $"Registered: {string.Join(", ", _repositories.Select(r => r.Name))}");

    public IReadOnlyDictionary<string, string> GetAvailableRepositories() =>
        _repositories.ToDictionary(r => r.Name, r => r.RepositoryType, StringComparer.OrdinalIgnoreCase);
}
