namespace AiOps.McpServer.Configuration;

public sealed class LogRepositoryConfig
{
    /// <summary>SerilogFile | Sql | AzureMonitor</summary>
    public string Type { get; set; } = string.Empty;

    // ── Serilog file ─────────────────────────────────────────────────────────
    /// <summary>Directory that contains the log files.</summary>
    public string? Directory { get; set; }

    /// <summary>File glob pattern within Directory, e.g. "*.clef" or "app-*.json".</summary>
    public string FilePattern { get; set; } = "*.clef";

    // ── SQL ──────────────────────────────────────────────────────────────────
    public string? ConnectionString { get; set; }

    /// <summary>
    /// ADO.NET provider invariant name used to resolve a <see cref="System.Data.Common.DbProviderFactory"/>
    /// via <see cref="System.Data.Common.DbProviderFactories.GetFactory(string)"/>.
    /// The provider package must be referenced so it auto-registers itself.
    /// Common values:
    /// <list type="bullet">
    ///   <item><c>Microsoft.Data.SqlClient</c> — SQL Server</item>
    ///   <item><c>Npgsql</c> — PostgreSQL</item>
    ///   <item><c>MySqlConnector</c> — MySQL / MariaDB</item>
    ///   <item><c>Microsoft.Data.Sqlite</c> — SQLite</item>
    /// </list>
    /// </summary>
    public string ProviderName { get; set; } = "Microsoft.Data.SqlClient";

    /// <summary>
    /// Controls identifier quoting and the row-limit clause emitted in the query.
    /// <list type="bullet">
    ///   <item><c>SqlServer</c> — <c>[...]</c> brackets, <c>SELECT TOP (@maxResults)</c></item>
    ///   <item><c>MySql</c>    — backtick quoting, <c>LIMIT @maxResults</c></item>
    ///   <item><c>Ansi</c>     — <c>"..."</c> double-quotes, <c>LIMIT @maxResults</c>
    ///         (PostgreSQL, SQLite, Oracle, etc.)</item>
    /// </list>
    /// Defaults to <c>Ansi</c>.
    /// </summary>
    public string SqlDialect { get; set; } = "Ansi";

    /// <summary>Optional schema prefix. Leave empty for databases that don't use schemas (e.g. SQLite).</summary>
    public string SchemaName { get; set; } = "dbo";
    public string TableName { get; set; } = "Logs";

    // Column name overrides for non-standard Serilog SQL sink schemas
    public string TimestampColumn { get; set; } = "TimeStamp";
    public string LevelColumn { get; set; } = "Level";
    public string MessageColumn { get; set; } = "Message";
    public string MessageTemplateColumn { get; set; } = "MessageTemplate";
    public string ExceptionColumn { get; set; } = "Exception";
    public string PropertiesColumn { get; set; } = "Properties";

    // ── Azure Monitor ────────────────────────────────────────────────────────
    public string? WorkspaceId { get; set; }

    /// <summary>
    /// Kusto table to query. Defaults to "AppExceptions".
    /// Use "AppTraces" to query trace/log entries instead.
    /// </summary>
    public string LogsTable { get; set; } = "AppExceptions";

    /// <summary>Azure tenant ID — required when using client-credential auth.</summary>
    public string? TenantId { get; set; }
}
