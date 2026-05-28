using System.Text.Json.Serialization;

namespace AiOps.Agent.Models;

/// <summary>
/// The root JSON document written to disk after each analysis run.
/// </summary>
public sealed class AnalysisRun
{
    [JsonPropertyName("runId")]
    public string RunId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    /// <summary>Ordered record of every MCP tool call made during the run.</summary>
    [JsonPropertyName("toolCalls")]
    public List<ToolCallRecord> ToolCalls { get; set; } = [];

    /// <summary>The final Markdown analysis report produced by Claude.</summary>
    [JsonPropertyName("finalReport")]
    public string? FinalReport { get; set; }

    /// <summary>Non-null when the run failed; contains the exception type name.</summary>
    [JsonPropertyName("errorType")]
    public string? ErrorType { get; set; }

    /// <summary>Non-null when the run failed; contains the exception message.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A single MCP tool invocation recorded during an analysis run.
/// </summary>
public sealed class ToolCallRecord
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = "";

    /// <summary>Raw JSON arguments Claude passed to the tool.</summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = "{}";

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("calledAt")]
    public DateTimeOffset CalledAt { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}
