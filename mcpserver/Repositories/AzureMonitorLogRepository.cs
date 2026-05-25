using AiOps.McpServer.Configuration;
using AiOps.McpServer.Models;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace AiOps.McpServer.Repositories;

/// <summary>
/// Queries logs from an Azure Monitor Log Analytics workspace using Kusto queries.
/// Supports the built-in AppExceptions and AppTraces tables, as well as custom tables.
/// Authentication uses DefaultAzureCredential (env vars, managed identity, VS/CLI login).
/// </summary>
public sealed class AzureMonitorLogRepository(string name, LogRepositoryConfig config)
    : ILogRepository
{
    public string Name { get; } = name;
    public string RepositoryType => "AzureMonitor";

    public async Task<IReadOnlyList<LogEntry>> QueryErrorsAsync(
        LogQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.WorkspaceId))
            throw new InvalidOperationException($"Repository '{Name}': WorkspaceId is not configured.");

        var credential = string.IsNullOrWhiteSpace(config.TenantId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = config.TenantId });

        var client = new LogsQueryClient(credential);

        var kusto = BuildKustoQuery(options);
        var timeRange = new QueryTimeRange(options.From, options.To);

        var response = await client.QueryWorkspaceAsync(
            config.WorkspaceId,
            kusto,
            timeRange,
            cancellationToken: cancellationToken);

        return MapResults(response.Value);
    }

    private string BuildKustoQuery(LogQueryOptions options)
    {
        var table = config.LogsTable;
        var isExceptionsTable = table.Equals("AppExceptions", StringComparison.OrdinalIgnoreCase);

        var lines = new List<string> { table };

        // Severity filter
        if (options.Levels.Length > 0)
        {
            // Azure Monitor severity: 0=Verbose,1=Information,2=Warning,3=Error,4=Critical
            var hasError = options.Levels.Any(l =>
                l.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                l.Equals("Fatal", StringComparison.OrdinalIgnoreCase) ||
                l.Equals("Critical", StringComparison.OrdinalIgnoreCase));

            if (hasError)
                lines.Add("| where SeverityLevel >= 3");
        }

        // Free-text search
        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            if (isExceptionsTable)
                lines.Add($"| where OuterMessage contains \"{EscapeKusto(options.SearchTerm)}\"" +
                          $" or InnermostMessage contains \"{EscapeKusto(options.SearchTerm)}\"" +
                          $" or ExceptionType contains \"{EscapeKusto(options.SearchTerm)}\"");
            else
                lines.Add($"| where Message contains \"{EscapeKusto(options.SearchTerm)}\"");
        }

        // Projection
        if (isExceptionsTable)
            lines.Add("| project TimeGenerated, ExceptionType, OuterMessage, InnermostMessage, " +
                      "OuterType, Method, Assembly, ProblemId, OperationId, Details, SeverityLevel");
        else
            lines.Add("| project TimeGenerated, Message, SeverityLevel, OperationId, Properties");

        lines.Add("| order by TimeGenerated desc");
        lines.Add($"| take {options.MaxResults}");

        return string.Join("\n", lines);
    }

    private static IReadOnlyList<LogEntry> MapResults(LogsQueryResult result)
    {
        var entries = new List<LogEntry>();

        foreach (var table in result.AllTables)
        {
            var colIndex = table.Columns
                .Select((c, i) => (c.Name, i))
                .ToDictionary(t => t.Name, t => t.i, StringComparer.OrdinalIgnoreCase);

            foreach (var row in table.Rows)
            {
                var isExceptions = colIndex.ContainsKey("ExceptionType");
                var entry = isExceptions
                    ? MapExceptionRow(row, colIndex)
                    : MapTraceRow(row, colIndex);

                if (entry is not null)
                    entries.Add(entry);
            }
        }

        return entries;
    }

    private static LogEntry? MapExceptionRow(
        LogsTableRow row,
        Dictionary<string, int> col)
    {
        try
        {
            var timestamp = GetDateTimeOffset(row, col, "TimeGenerated");
            var exceptionType = GetString(row, col, "ExceptionType");
            var outerMessage = GetString(row, col, "OuterMessage");
            var innermostMessage = GetString(row, col, "InnermostMessage");
            var details = GetString(row, col, "Details");

            var exceptionText = string.IsNullOrWhiteSpace(details)
                ? $"{exceptionType}: {outerMessage}"
                : details;

            var props = new Dictionary<string, object?>
            {
                ["OperationId"] = GetString(row, col, "OperationId"),
                ["ProblemId"] = GetString(row, col, "ProblemId"),
                ["Method"] = GetString(row, col, "Method"),
                ["Assembly"] = GetString(row, col, "Assembly")
            };

            return new LogEntry
            {
                Timestamp = timestamp,
                Level = "Error",
                Message = outerMessage ?? innermostMessage ?? exceptionType ?? string.Empty,
                Exception = exceptionText,
                ExceptionType = exceptionType,
                ExceptionMessage = outerMessage ?? innermostMessage,
                Properties = props
            };
        }
        catch
        {
            return null;
        }
    }

    private static LogEntry? MapTraceRow(
        LogsTableRow row,
        Dictionary<string, int> col)
    {
        try
        {
            var severity = col.TryGetValue("SeverityLevel", out var si)
                ? row[si]?.ToString()
                : null;

            var level = severity switch
            {
                "4" => "Critical",
                "3" => "Error",
                "2" => "Warning",
                _ => "Error"
            };

            return new LogEntry
            {
                Timestamp = GetDateTimeOffset(row, col, "TimeGenerated"),
                Level = level,
                Message = GetString(row, col, "Message") ?? string.Empty,
                Properties = new Dictionary<string, object?>
                {
                    ["OperationId"] = GetString(row, col, "OperationId")
                }
            };
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset GetDateTimeOffset(
        LogsTableRow row,
        Dictionary<string, int> col,
        string columnName)
    {
        if (!col.TryGetValue(columnName, out var idx)) return DateTimeOffset.UtcNow;
        return row[idx] is DateTimeOffset dto ? dto
            : row[idx] is DateTime dt ? new DateTimeOffset(dt, TimeSpan.Zero)
            : DateTimeOffset.TryParse(row[idx]?.ToString(), out var parsed) ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static string? GetString(LogsTableRow row, Dictionary<string, int> col, string columnName) =>
        col.TryGetValue(columnName, out var idx) ? row[idx]?.ToString() : null;

    private static string EscapeKusto(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
