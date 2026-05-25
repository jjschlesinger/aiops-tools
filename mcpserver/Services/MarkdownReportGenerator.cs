using System.Text;
using AiOps.McpServer.Models;

namespace AiOps.McpServer.Services;

public sealed class MarkdownReportGenerator
{
    private const int MaxStackTraceLines = 15;
    private const int MaxEntriesPerGroup = 10;

    public string Generate(
        string repositoryName,
        LogQueryOptions options,
        IReadOnlyList<LogEntry> entries)
    {
        var groups = GroupByExceptionType(entries);
        var sb = new StringBuilder();

        WriteHeader(sb, repositoryName, options, entries.Count);
        WriteExecutiveSummary(sb, groups);
        WriteExceptionDetails(sb, groups);
        WriteTimeline(sb, entries, options);
        WriteInvestigationAreas(sb, groups);
        WriteAnalysisTemplate(sb);

        return sb.ToString();
    }

    // ── Grouping ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<ExceptionGroup> GroupByExceptionType(IReadOnlyList<LogEntry> entries)
    {
        return entries
            .GroupBy(e => e.ExceptionType ?? ClassifyByMessage(e.Message))
            .Select(g =>
            {
                var sorted = g.OrderByDescending(e => e.Timestamp).ToList();
                var sample = sorted.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Exception)) ?? sorted[0];

                return new ExceptionGroup
                {
                    ExceptionType = g.Key,
                    Count = g.Count(),
                    FirstSeen = g.Min(e => e.Timestamp),
                    LastSeen = g.Max(e => e.Timestamp),
                    SampleMessage = sample.ExceptionMessage ?? sample.Message,
                    SampleStackTrace = TrimStackTrace(sample.Exception),
                    Entries = sorted.Take(MaxEntriesPerGroup).ToList()
                };
            })
            .OrderByDescending(g => g.Count)
            .ToList();
    }

    private static string ClassifyByMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown";
        return message.Length > 60 ? message[..60] + "…" : message;
    }

    private static string? TrimStackTrace(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace)) return null;
        var lines = stackTrace.Split('\n');
        return lines.Length <= MaxStackTraceLines
            ? stackTrace.Trim()
            : string.Join('\n', lines.Take(MaxStackTraceLines)) + $"\n   ... ({lines.Length - MaxStackTraceLines} more lines)";
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private static void WriteHeader(
        StringBuilder sb,
        string repositoryName,
        LogQueryOptions options,
        int totalCount)
    {
        sb.AppendLine("# Log Error Analysis Report");
        sb.AppendLine();
        sb.AppendLine($"| | |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| **Generated** | {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC |");
        sb.AppendLine($"| **Repository** | `{repositoryName}` |");
        sb.AppendLine($"| **From** | {options.From:yyyy-MM-dd HH:mm:ss} UTC |");
        sb.AppendLine($"| **To** | {options.To:yyyy-MM-dd HH:mm:ss} UTC |");
        sb.AppendLine($"| **Severity filter** | {string.Join(", ", options.Levels)} |");
        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
            sb.AppendLine($"| **Search term** | `{options.SearchTerm}` |");
        sb.AppendLine($"| **Total errors found** | **{totalCount}** |");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void WriteExecutiveSummary(StringBuilder sb, IReadOnlyList<ExceptionGroup> groups)
    {
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();

        if (groups.Count == 0)
        {
            sb.AppendLine("> No errors found for the specified criteria.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Exception Type | Count | First Seen | Last Seen |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var g in groups)
        {
            sb.AppendLine(
                $"| `{EscapeTable(g.ExceptionType)}` " +
                $"| {g.Count} " +
                $"| {g.FirstSeen:yyyy-MM-dd HH:mm:ss} " +
                $"| {g.LastSeen:yyyy-MM-dd HH:mm:ss} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void WriteExceptionDetails(StringBuilder sb, IReadOnlyList<ExceptionGroup> groups)
    {
        if (groups.Count == 0) return;

        sb.AppendLine("## Exception Details");
        sb.AppendLine();

        foreach (var g in groups)
        {
            sb.AppendLine($"### `{g.ExceptionType}` — {g.Count} occurrence{(g.Count == 1 ? "" : "s")}");
            sb.AppendLine();
            sb.AppendLine($"- **First seen:** {g.FirstSeen:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"- **Last seen:** {g.LastSeen:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"- **Frequency:** {FrequencyLabel(g)}");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(g.SampleMessage))
            {
                sb.AppendLine("**Sample message:**");
                sb.AppendLine();
                sb.AppendLine($"> {g.SampleMessage}");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(g.SampleStackTrace))
            {
                sb.AppendLine("**Sample stack trace:**");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(g.SampleStackTrace);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (g.Entries.Count > 0)
            {
                sb.AppendLine("**Recent occurrences:**");
                sb.AppendLine();
                sb.AppendLine("| Timestamp | Message |");
                sb.AppendLine("|---|---|");
                foreach (var e in g.Entries)
                {
                    var msg = Truncate(e.Message, 80);
                    sb.AppendLine($"| {e.Timestamp:yyyy-MM-dd HH:mm:ss} | {EscapeTable(msg)} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }
    }

    private static void WriteTimeline(
        StringBuilder sb,
        IReadOnlyList<LogEntry> entries,
        LogQueryOptions options)
    {
        if (entries.Count == 0) return;

        sb.AppendLine("## Error Timeline");
        sb.AppendLine();

        var span = options.To - options.From;
        var bucketHours = span.TotalHours <= 24 ? 1 : span.TotalHours <= 168 ? 6 : 24;
        var buckets = new SortedDictionary<DateTimeOffset, int>();

        var bucketStart = options.From;
        while (bucketStart < options.To)
        {
            buckets[bucketStart] = 0;
            bucketStart = bucketStart.AddHours(bucketHours);
        }

        foreach (var e in entries)
        {
            var key = buckets.Keys.LastOrDefault(k => k <= e.Timestamp);
            if (key != default)
                buckets[key]++;
        }

        var maxCount = buckets.Values.Max();
        var barWidth = 30;

        sb.AppendLine($"| Period | Count | Distribution |");
        sb.AppendLine($"|---|---|---|");
        foreach (var (period, count) in buckets)
        {
            var filled = maxCount == 0 ? 0 : (int)Math.Round((double)count / maxCount * barWidth);
            var bar = new string('█', filled) + new string('░', barWidth - filled);
            sb.AppendLine($"| {period:MM-dd HH:mm} | {count,4} | `{bar}` |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void WriteInvestigationAreas(StringBuilder sb, IReadOnlyList<ExceptionGroup> groups)
    {
        if (groups.Count == 0) return;

        sb.AppendLine("## Recommended Investigation Areas");
        sb.AppendLine();

        var i = 1;
        foreach (var g in groups)
        {
            var hint = GetInvestigationHint(g.ExceptionType);
            sb.AppendLine($"{i++}. **`{g.ExceptionType}`** ({g.Count}x)  ");
            sb.AppendLine($"   {hint}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void WriteAnalysisTemplate(StringBuilder sb)
    {
        sb.AppendLine("## Analysis & Recommended Fixes");
        sb.AppendLine();
        sb.AppendLine("> _This section is populated by the LLM based on the exception details above._");
        sb.AppendLine();
        sb.AppendLine("### Root Cause Analysis");
        sb.AppendLine();
        sb.AppendLine("<!-- LLM: describe root causes per exception group -->");
        sb.AppendLine();
        sb.AppendLine("### Fix Recommendations");
        sb.AppendLine();
        sb.AppendLine("<!-- LLM: numbered fix steps per exception group -->");
        sb.AppendLine();
        sb.AppendLine("### Priority Order");
        sb.AppendLine();
        sb.AppendLine("<!-- LLM: order fixes by business impact and risk -->");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FrequencyLabel(ExceptionGroup g)
    {
        var span = g.LastSeen - g.FirstSeen;
        if (span.TotalMinutes < 1) return "burst (< 1 min window)";
        var rate = g.Count / span.TotalHours;
        return rate > 60 ? $"~{rate:F0}/hr (high frequency)"
            : rate > 1 ? $"~{rate:F1}/hr"
            : $"~{g.Count / span.TotalDays:F1}/day";
    }

    private static string GetInvestigationHint(string exceptionType) => exceptionType switch
    {
        var t when t.Contains("NullReference") =>
            "Likely a null guard missing or an optional dependency not resolved before use.",
        var t when t.Contains("Timeout") =>
            "Check downstream service latency, connection pool exhaustion, or misconfigured timeout values.",
        var t when t.Contains("SqlException") || t.Contains("DbException") =>
            "Inspect SQL query plans, index coverage, connection string configuration, and connection pool limits.",
        var t when t.Contains("OutOfMemory") =>
            "Profile heap allocations. Look for large object retentions or unbounded collection growth.",
        var t when t.Contains("Unauthorize") || t.Contains("Forbidden") || t.Contains("Authentication") =>
            "Verify token lifetimes, certificate validity, and permission assignments.",
        var t when t.Contains("Http") || t.Contains("Network") || t.Contains("Socket") =>
            "Check external service availability, TLS configuration, and firewall rules.",
        var t when t.Contains("Deserializ") || t.Contains("JsonException") || t.Contains("Format") =>
            "Inspect API contract changes or unexpected payloads from upstream services.",
        _ => "Review stack traces above; correlate with deployment timestamps and upstream service changes."
    };

    private static string EscapeTable(string? value) =>
        (value ?? string.Empty).Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
