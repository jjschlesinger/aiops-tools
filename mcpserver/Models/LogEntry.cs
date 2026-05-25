namespace AiOps.McpServer.Models;

public sealed record LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? MessageTemplate { get; init; }

    /// <summary>Full exception text including type, message, and stack trace.</summary>
    public string? Exception { get; init; }

    /// <summary>Parsed exception type name, e.g. "System.NullReferenceException".</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Top-level exception message only.</summary>
    public string? ExceptionMessage { get; init; }

    public IReadOnlyDictionary<string, object?> Properties { get; init; } =
        new Dictionary<string, object?>();
}
