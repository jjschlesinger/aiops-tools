using System.Text.Json;
using AiOps.McpServer.Configuration;
using AiOps.McpServer.Models;

namespace AiOps.McpServer.Repositories;

/// <summary>
/// Reads Serilog Compact Log Event Format (CLEF) files — one JSON object per line.
/// Compatible with Serilog.Formatting.Compact and Serilog.Sinks.File.
/// </summary>
public sealed class SerilogFileLogRepository(string name, LogRepositoryConfig config)
    : ILogRepository
{
    public string Name { get; } = name;
    public string RepositoryType => "SerilogFile";

    public async Task<IReadOnlyList<LogEntry>> QueryErrorsAsync(
        LogQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.Directory))
            throw new InvalidOperationException($"Repository '{Name}': Directory is not configured.");

        var files = GetMatchingFiles();
        var results = new List<LogEntry>();

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested) break;

            using var reader = new StreamReader(file);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var entry = ParseClefLine(line);
                if (entry is null) continue;
                if (!MatchesQuery(entry, options)) continue;

                results.Add(entry);
                if (results.Count >= options.MaxResults) goto done;
            }
        }

        done:
        return results.OrderByDescending(e => e.Timestamp).ToList();
    }

    private IEnumerable<string> GetMatchingFiles()
    {
        if (!System.IO.Directory.Exists(config.Directory))
            return [];

        return System.IO.Directory
            .EnumerateFiles(config.Directory, config.FilePattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc);
    }

    private static LogEntry? ParseClefLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var timestamp = root.TryGetProperty("@t", out var t)
                ? DateTimeOffset.Parse(t.GetString()!)
                : DateTimeOffset.UtcNow;

            var level = root.TryGetProperty("@l", out var l)
                ? l.GetString() ?? "Information"
                : "Information";

            var message = root.TryGetProperty("@m", out var m)
                ? m.GetString() ?? string.Empty
                : root.TryGetProperty("@mt", out var mt)
                    ? mt.GetString() ?? string.Empty
                    : string.Empty;

            var messageTemplate = root.TryGetProperty("@mt", out var mtProp)
                ? mtProp.GetString()
                : null;

            var exception = root.TryGetProperty("@x", out var x) ? x.GetString() : null;

            var (exType, exMsg) = ParseExceptionHeader(exception);

            var properties = new Dictionary<string, object?>();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.StartsWith('@')) continue;
                properties[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var i) ? (object?)i : prop.Value.GetDouble(),
                    JsonValueKind.True or JsonValueKind.False => prop.Value.GetBoolean(),
                    _ => prop.Value.GetRawText()
                };
            }

            return new LogEntry
            {
                Timestamp = timestamp,
                Level = level,
                Message = message,
                MessageTemplate = messageTemplate,
                Exception = exception,
                ExceptionType = exType,
                ExceptionMessage = exMsg,
                Properties = properties
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesQuery(LogEntry entry, LogQueryOptions options)
    {
        if (entry.Timestamp < options.From || entry.Timestamp > options.To)
            return false;

        if (options.Levels.Length > 0 &&
            !options.Levels.Any(l => string.Equals(l, entry.Level, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            var term = options.SearchTerm;
            var hit = entry.Message.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (entry.Exception?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (entry.ExceptionType?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!hit) return false;
        }

        return true;
    }

    // "System.NullReferenceException: Object reference not set..." → ("System.NullReferenceException", "Object reference...")
    internal static (string? type, string? message) ParseExceptionHeader(string? exceptionText)
    {
        if (string.IsNullOrWhiteSpace(exceptionText))
            return (null, null);

        var firstLine = exceptionText.Split('\n', 2)[0].Trim();
        var colonIdx = firstLine.IndexOf(':');
        if (colonIdx <= 0)
            return (firstLine, null);

        var type = firstLine[..colonIdx].Trim();
        var msg = firstLine[(colonIdx + 1)..].Trim();

        // Validate that "type" looks like a namespace-qualified name
        return type.Contains(' ') ? (null, firstLine) : (type, msg);
    }
}
