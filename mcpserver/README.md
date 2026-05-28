# AiOps MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server written in **.NET 10** that gives an LLM structured access to application log repositories. Connect it to Claude (or any MCP-compatible client), and the model can discover configured log sources, query recent errors, and produce Markdown analysis reports with timeline breakdowns, grouped stack traces, and fix-recommendation templates.

---

## Purpose

Ops engineers spend significant time manually sifting through logs after an incident. This server bridges the gap between raw log storage and an LLM's ability to reason about patterns — without exposing entire log files to the model. Instead, the server:

- **Filters and structures** log data (time window, severity, search term) before returning it
- **Groups and aggregates** exceptions so the model sees patterns, not noise
- **Generates opinionated Markdown reports** the model can summarise or hand directly to a human

The server itself performs no analysis; it is a tool substrate. Analysis quality depends on the LLM driving it.

---

## Quick start

```bash
cd mcpserver

# Restore packages
dotnet restore

# Configure your log repositories (see Configuration section below)
# then run — communicates over stdin/stdout
dotnet run
```

The server speaks MCP over **stdio**. To wire it to the AiOps agent:

```bash
cd ../agent
dotnet run          # agent spawns mcpserver automatically
```

To wire it to Claude Desktop, add an entry to your MCP config:

```json
{
  "mcpServers": {
    "aiops-logs": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/aiops/mcpserver", "--no-build"]
    }
  }
}
```

---

## Tools

### `list_log_repositories`

Lists every configured repository by name and type. Always call this first to discover what data sources are available before querying.

**Parameters:** none

**Example response:**
```
Available log repositories:

- **prod-serilog** (SerilogFile)
- **prod-sql** (Sql)
- **prod-azure** (AzureMonitor)
```

---

### `query_log_errors`

Returns raw log entries as structured JSON. Useful for interactive exploration before committing to a full report.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `repositoryName` | string | *(required)* | Name returned by `list_log_repositories` |
| `timeRangeHours` | int | `24` | How many hours back from now to query |
| `searchTerm` | string | — | Keyword filter applied to message and exception text |
| `maxResults` | int | `100` | Maximum entries to return (hard cap: 500) |
| `levels` | string | `"Error,Fatal,Critical"` | Comma-separated severity levels to include |

**Returns:** JSON object with query metadata and an array of `LogEntry` objects, each containing timestamp, level, message, exception type/message, stack trace, and structured properties.

---

### `generate_analysis_report`

Queries a repository and produces a comprehensive Markdown analysis report. Accepts the same parameters as `query_log_errors`.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `repositoryName` | string | *(required)* | Name returned by `list_log_repositories` |
| `timeRangeHours` | int | `24` | Hours back to query |
| `searchTerm` | string | — | Keyword filter |
| `maxResults` | int | `200` | Maximum entries (hard cap: 500) |
| `levels` | string | `"Error,Fatal,Critical"` | Severity levels |

**Report sections:**
- **Executive summary** — total error count, unique exception types, time window
- **Per-exception-type detail** — count, first/last seen, representative stack traces
- **Error timeline** — distribution histogram showing when errors spiked
- **Investigation hints** — per-exception-type suggestions based on the error text
- **Analysis template** — a stub for the LLM (or a human) to fill in with root-cause findings and recommended fixes

---

## Configuration

All repositories are defined in `appsettings.json` under `LogRepositories.Repositories`. Each key becomes the repository name used in tool calls.

```json
{
  "LogRepositories": {
    "Repositories": {
      "<repository-name>": {
        "Type": "<SerilogFile | Sql | AzureMonitor>",
        ...type-specific options...
      }
    }
  }
}
```

### Serilog CLEF files

Reads [Serilog Compact Log Event Format](https://github.com/serilog/serilog-formatting-compact) files — one JSON object per line.

```json
"prod-serilog": {
  "Type": "SerilogFile",
  "Directory": "C:\\Logs\\Application",
  "FilePattern": "*.clef"
}
```

| Key | Required | Default | Description |
|---|---|---|---|
| `Directory` | ✓ | — | Path to directory containing log files |
| `FilePattern` | | `"*.clef"` | Glob pattern for matching files within that directory |

---

### SQL databases

Queries any ADO.NET-compatible database. The Serilog SQL Sink schema is the default; column names are all configurable.

```json
"prod-sql": {
  "Type": "Sql",
  "ProviderName": "Microsoft.Data.SqlClient",
  "SqlDialect": "SqlServer",
  "ConnectionString": "Server=prod-db;Database=AppLogs;Integrated Security=true;TrustServerCertificate=true;",
  "SchemaName": "dbo",
  "TableName": "Logs",
  "TimestampColumn": "TimeStamp",
  "LevelColumn": "Level",
  "MessageColumn": "Message",
  "ExceptionColumn": "Exception",
  "PropertiesColumn": "Properties"
}
```

| Key | Required | Default | Description |
|---|---|---|---|
| `ConnectionString` | ✓ | — | ADO.NET connection string |
| `ProviderName` | ✓ | — | ADO.NET provider invariant name (see table below) |
| `SqlDialect` | ✓ | — | Controls identifier quoting and LIMIT syntax |
| `SchemaName` | | `"dbo"` | Schema prefix; leave empty for SQLite |
| `TableName` | | `"Logs"` | Log table name |
| `TimestampColumn` | | `"TimeStamp"` | Timestamp column |
| `LevelColumn` | | `"Level"` | Severity level column |
| `MessageColumn` | | `"Message"` | Rendered message column |
| `MessageTemplateColumn` | | `"MessageTemplate"` | Message template column |
| `ExceptionColumn` | | `"Exception"` | Exception column |
| `PropertiesColumn` | | `"Properties"` | Structured properties column (JSON) |

**Supported providers and dialects:**

| Database | `ProviderName` | `SqlDialect` | NuGet package |
|---|---|---|---|
| SQL Server | `Microsoft.Data.SqlClient` | `SqlServer` | `Microsoft.Data.SqlClient` *(bundled)* |
| PostgreSQL | `Npgsql` | `Ansi` | `Npgsql` |
| MySQL / MariaDB | `MySqlConnector` | `MySql` | `MySqlConnector` |
| SQLite | `Microsoft.Data.Sqlite` | `Ansi` | `Microsoft.Data.Sqlite` |

> **Note:** Only `Microsoft.Data.SqlClient` is included in the project by default. To use PostgreSQL, MySQL, or SQLite, uncomment the relevant `<PackageReference>` in `AiOps.McpServer.csproj` and `dotnet restore`.

---

### Azure Monitor (Log Analytics)

Queries an Azure Monitor Log Analytics workspace using Kusto.

```json
"prod-azure": {
  "Type": "AzureMonitor",
  "WorkspaceId": "00000000-0000-0000-0000-000000000000",
  "LogsTable": "AppExceptions",
  "TenantId": "00000000-0000-0000-0000-000000000000"
}
```

| Key | Required | Default | Description |
|---|---|---|---|
| `WorkspaceId` | ✓ | — | UUID of the Log Analytics workspace |
| `LogsTable` | | `"AppExceptions"` | Kusto table to query: `AppExceptions` or `AppTraces` |
| `TenantId` | | — | Azure tenant ID; used to scope `DefaultAzureCredential`. Omit to use the default tenant |

**Authentication** uses `DefaultAzureCredential`, which tries (in order): environment variables (`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`), workload identity, managed identity, Visual Studio, Azure CLI, and Azure PowerShell. No extra configuration is needed in most Azure-hosted environments.

---

## Project structure

```
mcpserver/
├── AiOps.McpServer.csproj
├── appsettings.json                    # repository configuration
├── Program.cs                          # host setup, DI wiring, MCP server startup
│
├── Configuration/
│   ├── LogRepositoryConfig.cs          # per-repository config schema
│   └── LogRepositorySettings.cs        # top-level settings container
│
├── Models/
│   ├── LogEntry.cs                     # single log entry (timestamp, level, message, exception, properties)
│   ├── LogQueryOptions.cs              # query filter parameters
│   └── ExceptionGroup.cs              # aggregated group of similar exceptions
│
├── Repositories/
│   ├── ILogRepository.cs               # query interface
│   ├── ILogRepositoryFactory.cs        # factory interface
│   ├── LogRepositoryFactory.cs         # DI-based factory implementation
│   ├── SerilogFileLogRepository.cs     # CLEF file reader
│   ├── SqlLogRepository.cs             # ADO.NET / Dapper query engine
│   └── AzureMonitorLogRepository.cs    # Kusto / Log Analytics client
│
├── Services/
│   └── MarkdownReportGenerator.cs      # produces the analysis report Markdown
│
├── Extensions/
│   └── LogRepositoryServiceExtensions.cs  # AddLogRepositories() DI extension
│
└── Tools/
    └── LogAnalysisTool.cs              # MCP tool definitions ([McpTool] methods)
```

---

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| `ModelContextProtocol` | 0.1.0-preview | MCP server SDK (stdio transport) |
| `Microsoft.Extensions.Hosting` | 9.0.5 | Generic Host, DI, configuration |
| `Dapper` | 2.1.35 | Lightweight SQL query mapping |
| `Microsoft.Data.SqlClient` | 5.2.2 | SQL Server ADO.NET driver |
| `Azure.Monitor.Query` | 1.4.0 | Azure Monitor Log Analytics client |
| `Azure.Identity` | 1.13.1 | `DefaultAzureCredential` for Azure auth |
