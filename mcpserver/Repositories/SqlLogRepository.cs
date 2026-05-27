using System.Data.Common;
using System.Text;
using System.Text.Json;
using AiOps.McpServer.Configuration;
using AiOps.McpServer.Models;
using Dapper;

namespace AiOps.McpServer.Repositories;

/// <summary>
/// Queries logs from any ADO.NET-compatible database via <see cref="DbConnection"/>.
/// The concrete driver is resolved at runtime through <see cref="DbProviderFactories"/>,
/// so no hard dependency on a specific client library exists in this class.
/// The query syntax adapts to the configured <see cref="LogRepositoryConfig.SqlDialect"/>.
/// </summary>
public sealed class SqlLogRepository(string name, LogRepositoryConfig config)
    : ILogRepository
{
    public string Name { get; } = name;
    public string RepositoryType => "Sql";

    public async Task<IReadOnlyList<LogEntry>> QueryErrorsAsync(
        LogQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
            throw new InvalidOperationException($"Repository '{Name}': ConnectionString is not configured.");

        if (string.IsNullOrWhiteSpace(config.ProviderName))
            throw new InvalidOperationException($"Repository '{Name}': ProviderName is not configured.");

        var sql = BuildQuery(options);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<SqlLogRow>(
            new CommandDefinition(
                sql,
                new
                {
                    from = options.From.UtcDateTime,
                    to = options.To.UtcDateTime,
                    maxResults = options.MaxResults,
                    searchTerm = string.IsNullOrWhiteSpace(options.SearchTerm)
                        ? null
                        : $"%{options.SearchTerm}%"
                },
                cancellationToken: cancellationToken));

        return rows.Select(MapToEntry).ToList();
    }

    // ── Connection factory ────────────────────────────────────────────────────

    private DbConnection CreateConnection()
    {
        DbProviderFactory factory;
        try
        {
            factory = DbProviderFactories.GetFactory(config.ProviderName);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Repository '{Name}': ADO.NET provider '{config.ProviderName}' is not registered. " +
                $"Ensure the NuGet package is referenced and auto-registers with DbProviderFactories. " +
                $"If needed, call DbProviderFactories.RegisterFactory(\"{config.ProviderName}\", ...) in Program.cs.",
                ex);
        }

        var connection = factory.CreateConnection()
            ?? throw new InvalidOperationException(
                $"Repository '{Name}': provider '{config.ProviderName}' returned null from CreateConnection().");

        connection.ConnectionString = config.ConnectionString;
        return connection;
    }

    // ── Query builder ─────────────────────────────────────────────────────────

    internal string BuildQuery(LogQueryOptions options)
    {
        var (oq, cq) = DialectQuotes(config.SqlDialect);
        var isSqlServer = config.SqlDialect.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
        var isMySql = config.SqlDialect.Equals("MySql", StringComparison.OrdinalIgnoreCase);

        // Local helper — wraps an identifier in the dialect's quote characters.
        string Q(string id) => $"{oq}{id}{cq}";

        var table = string.IsNullOrWhiteSpace(config.SchemaName)
            ? Q(config.TableName)
            : $"{Q(config.SchemaName)}.{Q(config.TableName)}";

        var sb = new StringBuilder();

        // SELECT [TOP n] …
        sb.Append("SELECT ");
        if (isSqlServer)
            sb.Append("TOP (@maxResults) ");

        sb.AppendLine();
        sb.AppendLine($"    {Q(config.TimestampColumn)}      AS Timestamp,");
        sb.AppendLine($"    {Q(config.LevelColumn)}           AS Level,");
        sb.AppendLine($"    {Q(config.MessageColumn)}         AS Message,");
        sb.AppendLine($"    {Q(config.MessageTemplateColumn)} AS MessageTemplate,");
        sb.AppendLine($"    {Q(config.ExceptionColumn)}       AS Exception,");
        sb.AppendLine($"    {Q(config.PropertiesColumn)}      AS Properties");
        sb.AppendLine($"FROM {table}");

        // WHERE …
        sb.AppendLine($"WHERE {Q(config.TimestampColumn)} >= @from");
        sb.AppendLine($"  AND {Q(config.TimestampColumn)} <= @to");

        if (options.Levels.Length > 0)
        {
            // Embed level literals directly — safe because they come from config, not user input.
            var levels = string.Join(", ", options.Levels.Select(l => $"'{l.Replace("'", "''")}'"));
            sb.AppendLine($"  AND {Q(config.LevelColumn)} IN ({levels})");
        }

        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            sb.AppendLine( "  AND (");
            sb.AppendLine($"      {Q(config.MessageColumn)}   LIKE @searchTerm");
            sb.AppendLine($"   OR {Q(config.ExceptionColumn)} LIKE @searchTerm");
            sb.AppendLine( "  )");
        }

        sb.AppendLine($"ORDER BY {Q(config.TimestampColumn)} DESC");

        // Row-limit suffix (everything except SQL Server which uses TOP)
        if (!isSqlServer)
            sb.AppendLine(isMySql ? "LIMIT @maxResults" : "LIMIT @maxResults");
        //  Oracle / DB2 users can switch to: FETCH FIRST @maxResults ROWS ONLY

        return sb.ToString();
    }

    private static (string open, string close) DialectQuotes(string dialect) =>
        dialect switch
        {
            { } d when d.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) => ("[", "]"),
            { } d when d.Equals("MySql",     StringComparison.OrdinalIgnoreCase) => ("`", "`"),
            _ => ("\"", "\"")   // Ansi, PostgreSQL, SQLite, Oracle, etc.
        };

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static LogEntry MapToEntry(SqlLogRow row)
    {
        var (exType, exMsg) = SerilogFileLogRepository.ParseExceptionHeader(row.Exception);

        IReadOnlyDictionary<string, object?> props = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(row.Properties))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.Properties);
                props = doc.RootElement.EnumerateObject()
                    .ToDictionary(p => p.Name, p => (object?)p.Value.GetRawText());
            }
            catch { /* ignore malformed JSON properties */ }
        }

        return new LogEntry
        {
            Timestamp = row.Timestamp is not null
                ? new DateTimeOffset(DateTime.SpecifyKind(row.Timestamp.Value, DateTimeKind.Utc))
                : DateTimeOffset.UtcNow,
            Level = row.Level ?? "Unknown",
            Message = row.Message ?? string.Empty,
            MessageTemplate = row.MessageTemplate,
            Exception = row.Exception,
            ExceptionType = exType,
            ExceptionMessage = exMsg,
            Properties = props
        };
    }

    private sealed class SqlLogRow
    {
        public DateTime? Timestamp { get; set; }
        public string? Level { get; set; }
        public string? Message { get; set; }
        public string? MessageTemplate { get; set; }
        public string? Exception { get; set; }
        public string? Properties { get; set; }
    }
}
