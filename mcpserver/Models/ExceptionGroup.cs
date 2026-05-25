namespace AiOps.McpServer.Models;

public sealed record ExceptionGroup
{
    public string ExceptionType { get; init; } = "Unknown";
    public int Count { get; init; }
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; init; }
    public string? SampleMessage { get; init; }
    public string? SampleStackTrace { get; init; }
    public IReadOnlyList<LogEntry> Entries { get; init; } = [];
}
