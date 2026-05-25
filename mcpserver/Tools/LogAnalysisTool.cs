using System.ComponentModel;
using System.Text.Json;
using AiOps.McpServer.Models;
using AiOps.McpServer.Repositories;
using AiOps.McpServer.Services;
using ModelContextProtocol.Server;

namespace AiOps.McpServer.Tools;

[McpToolType]
public sealed class LogAnalysisTool(
    ILogRepositoryFactory repositoryFactory,
    MarkdownReportGenerator reportGenerator)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Tool: list_log_repositories ──────────────────────────────────────────

    [McpTool("list_log_repositories")]
    [Description(
        "Lists all configured log repositories available for querying. " +
        "Call this first to discover which repository names can be passed to the other tools.")]
    public string ListLogRepositories()
    {
        var repos = repositoryFactory.GetAvailableRepositories();

        if (repos.Count == 0)
            return "No log repositories are configured. " +
                   "Add entries under 'LogRepositories:Repositories' in appsettings.json.";

        var lines = repos.Select(kv => $"- **{kv.Key}** (type: {kv.Value})");
        return $"## Available Log Repositories\n\n{string.Join("\n", lines)}\n\n" +
               $"Use the repository name as the `repositoryName` parameter in other tools.";
    }

    // ── Tool: query_log_errors ───────────────────────────────────────────────

    [McpTool("query_log_errors")]
    [Description(
        "Queries a configured log repository for errors and exceptions within a time window. " +
        "Returns a JSON array of log entries with timestamps, levels, messages, exception types, " +
        "stack traces, and structured properties. " +
        "Use this for interactive exploration before generating a full report.")]
    public async Task<string> QueryLogErrors(
        [Description("Name of the repository to query (from list_log_repositories).")]
        string repositoryName,

        [Description("How many hours back from now to search. Defaults to 24.")]
        int timeRangeHours = 24,

        [Description("Optional keyword to filter by — matched against message and exception text.")]
        string? searchTerm = null,

        [Description("Maximum number of log entries to return. Defaults to 100, max 500.")]
        int maxResults = 100,

        [Description(
            "Comma-separated severity levels to include. " +
            "Defaults to 'Error,Fatal,Critical'. " +
            "Pass 'Error,Warning' to include warnings too.")]
        string levels = "Error,Fatal,Critical",

        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = BuildOptions(timeRangeHours, searchTerm, maxResults, levels);
            var repository = repositoryFactory.GetRepository(repositoryName);
            var entries = await repository.QueryErrorsAsync(options, cancellationToken);

            if (entries.Count == 0)
                return JsonSerializer.Serialize(new
                {
                    repositoryName,
                    query = new { options.From, options.To, options.Levels, options.SearchTerm },
                    totalFound = 0,
                    entries = Array.Empty<object>()
                }, JsonOptions);

            var result = new
            {
                repositoryName,
                repositoryType = repository.RepositoryType,
                query = new
                {
                    from = options.From,
                    to = options.To,
                    levels = options.Levels,
                    searchTerm = options.SearchTerm,
                    maxResults = options.MaxResults
                },
                totalFound = entries.Count,
                entries = entries.Select(MapEntryForOutput)
            };

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = ex.GetType().Name,
                message = ex.Message
            }, JsonOptions);
        }
    }

    // ── Tool: generate_analysis_report ───────────────────────────────────────

    [McpTool("generate_analysis_report")]
    [Description(
        "Queries a log repository for errors and generates a comprehensive Markdown analysis report. " +
        "The report includes an executive summary, per-exception-type breakdowns with stack traces, " +
        "an error timeline, investigation hints, and a template section for you to fill in " +
        "root-cause analysis and fix recommendations. " +
        "Ideal as the final step after using query_log_errors to understand the data.")]
    public async Task<string> GenerateAnalysisReport(
        [Description("Name of the repository to query (from list_log_repositories).")]
        string repositoryName,

        [Description("How many hours back from now to search. Defaults to 24.")]
        int timeRangeHours = 24,

        [Description("Optional keyword to filter results by.")]
        string? searchTerm = null,

        [Description("Maximum number of log entries to include in the report. Defaults to 200.")]
        int maxResults = 200,

        [Description(
            "Comma-separated severity levels to include. " +
            "Defaults to 'Error,Fatal,Critical'.")]
        string levels = "Error,Fatal,Critical",

        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = BuildOptions(timeRangeHours, searchTerm, maxResults, levels);
            var repository = repositoryFactory.GetRepository(repositoryName);
            var entries = await repository.QueryErrorsAsync(options, cancellationToken);

            return reportGenerator.Generate(repositoryName, options, entries);
        }
        catch (Exception ex)
        {
            return $"# Report Generation Failed\n\n" +
                   $"**Error:** `{ex.GetType().Name}`\n\n" +
                   $"**Message:** {ex.Message}\n\n" +
                   $"Verify the repository configuration and connectivity.";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LogQueryOptions BuildOptions(
        int timeRangeHours,
        string? searchTerm,
        int maxResults,
        string levels)
    {
        var parsedLevels = levels
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new LogQueryOptions
        {
            From = DateTimeOffset.UtcNow.AddHours(-Math.Abs(timeRangeHours)),
            To = DateTimeOffset.UtcNow,
            Levels = parsedLevels.Length > 0 ? parsedLevels : ["Error", "Fatal", "Critical"],
            SearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.Trim(),
            MaxResults = Math.Clamp(maxResults, 1, 500)
        };
    }

    private static object MapEntryForOutput(LogEntry e) => new
    {
        timestamp = e.Timestamp,
        level = e.Level,
        message = e.Message,
        exceptionType = e.ExceptionType,
        exceptionMessage = e.ExceptionMessage,
        stackTrace = e.Exception is null ? null : TrimLines(e.Exception, 20),
        properties = e.Properties.Count > 0 ? e.Properties : null
    };

    private static string TrimLines(string text, int maxLines)
    {
        var lines = text.Split('\n');
        return lines.Length <= maxLines
            ? text
            : string.Join('\n', lines.Take(maxLines)) + $"\n... ({lines.Length - maxLines} more lines)";
    }
}
