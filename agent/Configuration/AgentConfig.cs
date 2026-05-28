namespace AiOps.Agent.Configuration;

/// <summary>
/// Configuration for the AiOps agent loaded from appsettings.json → "Agent" section.
/// </summary>
public sealed class AgentConfig
{
    /// <summary>Executable used to launch the MCP server (e.g. "dotnet").</summary>
    public string McpServerCommand { get; set; } = "dotnet";

    /// <summary>
    /// Arguments passed to <see cref="McpServerCommand"/> (e.g. ["run", "--project", "../mcpserver"]).
    /// Defaults are provided by appsettings.json — the C# initialiser is intentionally empty so that
    /// .NET's ConfigurationBinder replaces (not appends to) this array when binding.
    /// </summary>
    public string[] McpServerArguments { get; set; } = [];

    /// <summary>Working directory for the MCP server process. Defaults to the agent's base directory.</summary>
    public string? McpServerWorkingDirectory { get; set; }

    /// <summary>Claude model name (e.g. "claude-opus-4-5-20250929").</summary>
    public string Model { get; set; } = "claude-opus-4-5-20250929";

    /// <summary>How many hours back to look for errors on each run. Supports fractional hours (e.g. 0.5 = last 30 min).</summary>
    public double TimeRangeHours { get; set; } = 24;

    /// <summary>Maximum log entries to retrieve per repository.</summary>
    public int MaxResults { get; set; } = 200;

    /// <summary>
    /// How often (in minutes) to run a new analysis.
    /// Supports fractional minutes (e.g. 0.5 = every 30 s).
    /// 0 or negative = one-shot: run once on startup then exit.
    /// </summary>
    public double IntervalMinutes { get; set; } = 60;

    /// <summary>Directory where per-run JSON result files are written.</summary>
    public string OutputDirectory { get; set; } = "results";

    /// <summary>Maximum tokens Claude may use in a single response turn.</summary>
    public int MaxTokensPerTurn { get; set; } = 8192;
}
