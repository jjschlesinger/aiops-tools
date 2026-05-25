namespace AiOps.McpServer.Models;

public sealed class LogQueryOptions
{
    public DateTimeOffset From { get; set; } = DateTimeOffset.UtcNow.AddHours(-24);
    public DateTimeOffset To { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Severity levels to include. Defaults to Error and Fatal/Critical.</summary>
    public string[] Levels { get; set; } = ["Error", "Fatal", "Critical"];

    /// <summary>Optional free-text filter applied to message and exception text.</summary>
    public string? SearchTerm { get; set; }

    public int MaxResults { get; set; } = 100;
}
