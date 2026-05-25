namespace AiOps.McpServer.Repositories;

public interface ILogRepositoryFactory
{
    /// <summary>Returns the <see cref="ILogRepository"/> registered under <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the name is not configured.</exception>
    ILogRepository GetRepository(string name);

    /// <summary>Returns all configured repository names mapped to their type strings.</summary>
    IReadOnlyDictionary<string, string> GetAvailableRepositories();
}
