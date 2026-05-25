namespace AiOps.McpServer.Configuration;

public sealed class LogRepositorySettings
{
    public Dictionary<string, LogRepositoryConfig> Repositories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
