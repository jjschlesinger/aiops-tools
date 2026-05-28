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

### Claude Desktop

Add an entry to `claude_desktop_config.json`
(macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`,
Windows: `%APPDATA%\Claude\claude_desktop_config.json`):

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

Restart Claude Desktop after saving. The `--no-build` flag assumes you have already run `dotnet build`; remove it if you want the CLI to build on each launch.

---

### VS Code (GitHub Copilot / agent mode)

VS Code reads MCP server definitions from a `.vscode/mcp.json` file in your workspace, or from User Settings.

**Option A — workspace file (recommended, checked into source control)**

Create `.vscode/mcp.json` at the root of your workspace:

```json
{
  "servers": {
    "aiops-logs": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "${workspaceFolder}/mcpserver", "--no-build"]
    }
  }
}
```

`${workspaceFolder}` is resolved by VS Code at runtime, so the path works for every contributor without edits.

**Option B — User Settings (applies to all workspaces)**

Open **Settings → (JSON)** (`Ctrl+Shift+P` → *Open User Settings (JSON)*) and add:

```json
{
  "mcp": {
    "servers": {
      "aiops-logs": {
        "type": "stdio",
        "command": "dotnet",
        "args": ["run", "--project", "/absolute/path/to/aiops/mcpserver", "--no-build"]
      }
    }
  }
}
```

After saving, open a Copilot Chat panel, switch to **Agent** mode, and the `aiops-logs` server will appear in the tools list.

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

## Security considerations

The MCP ecosystem is young and the security defaults across most implementations are poor. This section summarises the real risks and what you can do about them today.

### The threat landscape

MCP security is an active area of research. Between January and April 2026 alone, researchers disclosed over 40 CVEs across major MCP SDKs — roughly one every four days. Analysis of real-world implementations found that a single MCP server carries approximately a 9% exploitation risk; stack three servers together and the probability of at least one being exploitable exceeds 50%. These are not theoretical risks.

The protocol itself provides no authentication standard, no transport-level signing, and no capability scoping. Security is entirely the responsibility of the server builder and the system integrator.

### Attack vectors to understand

**Prompt injection via tool results**

This is the most prevalent and dangerous class. An LLM agent calls a tool, the tool fetches data from an external source (a log file, a database record, a web page), and that data contains adversarial instructions crafted to manipulate the model's next action. Because the model processes tool results as trusted context rather than raw data, it can be instructed to exfiltrate information, call additional tools, or change its behaviour entirely.

*This server returns log data that originates from your own applications, which is lower risk than servers that fetch arbitrary external content. However, log messages can contain user-supplied strings, so the risk is not zero.*

**Tool poisoning**

Tool descriptions and names are injected into the model's context window as trusted input. A malicious or compromised MCP server can embed hidden instructions inside its tool descriptions that the model reads and acts on — especially dangerous in clients that auto-approve tool calls without showing the description to a human first.

**Authentication gaps**

Many MCP servers ship with no authentication at all, or with trivially bypassable auth. CVE-2026-32211 (CVSS 9.1) — missing authentication in a major enterprise MCP integration — was disclosed in April 2026 and allowed unauthenticated access to API keys and configuration details.

*This server runs over stdio with no open port, which eliminates the network authentication surface entirely. There is nothing to authenticate against because there is no listener.*

**Path traversal and injection**

Analysis of over 2,600 MCP implementations found 82% use file operations prone to path traversal, 67% use APIs related to code injection, and 34% use APIs susceptible to command injection. These are classic vulnerability classes with a larger blast radius inside agent infrastructure because the model can chain tool calls autonomously.

**Privilege escalation via chaining**

Loosely scoped tools create lateral movement paths. An agent that has read access to one system and write access to another — with no scope limits enforced — can be directed to exfiltrate data from one and write it to the other. Each tool call is a step in a chain the model constructs.

---

### Mitigations

#### Run over stdio, not HTTP

This server uses stdio transport, which means:

- There is no open port for external actors to probe or authenticate against
- The server process only lives for the duration of the agent session — no persistent attack surface
- Tool calls can only reach what the process itself can reach

Prefer stdio over HTTP/SSE transports unless remote access is an explicit requirement.

#### Run in a container

Containerising the server is the single most effective blast-radius control available today:

- File operations are limited to explicitly mounted volumes — path traversal hits the container wall
- Network egress is blocked unless you open it — a compromised server cannot phone home or pivot to your internal network
- The server cannot inspect other processes, environment variables, or credentials outside the container

```bash
# Mount log directories read-only; the server only needs to read them
docker run --rm \
  -v /var/log/myapp:/logs:ro \
  -e ConnectionStrings__AppLogs="..." \
  ghcr.io/your-org/aiops-mcpserver
```

Run one container per MCP server rather than co-locating multiple servers. That way a compromised server cannot touch the others.

#### Least-privilege access

- Give the server's database credentials `SELECT`-only permissions — it has no need to write, update, or delete
- Mount log directories read-only (`:ro` in Docker, `ReadOnly = true` in Kubernetes `volumeMounts`)
- If using Azure Monitor, assign the `Log Analytics Reader` role, not `Contributor`
- Do not store credentials in `appsettings.json` in source control — use environment variables, Azure Key Vault references, or a secrets manager

#### Treat tool results as untrusted input

Do not assume log data is safe to pass directly to an LLM. Log messages can contain user-supplied strings. If your application logs HTTP request bodies, search queries, or form fields, those strings will appear in tool results.

Consider sanitising or truncating tool results before they reach the model's context, especially if your logs include content from external users.

#### Pin your server version

Lock the exact version of this server (and any other MCP servers you run) in your deployment config. Don't pull `latest`. If using container images, pin to a digest rather than a tag:

```bash
# Tag — mutable, can silently change
docker pull ghcr.io/your-org/aiops-mcpserver:latest

# Digest — immutable, exactly what you tested
docker pull ghcr.io/your-org/aiops-mcpserver@sha256:abc123...
```

The same principle applies to MCP server NuGet packages: pin exact versions in `AiOps.McpServer.csproj` and commit the lock file.

#### Never auto-approve tool calls with side effects

This server is read-only — it queries logs and returns data, it does not write, delete, or call external APIs. That makes auto-approval lower risk here than it would be for write-capable tools.

If you extend this server with write tools (e.g. creating tickets, posting alerts), require explicit human confirmation for those calls. Never auto-approve tool calls that have side effects.

#### Do not pass secrets through the protocol

Connection strings, API keys, and credentials belong in the server's configuration, not in tool arguments or tool results. If a tool result contains a secret value, that secret enters the model's context window and can be reproduced in model output.

---

### Deployment posture summary

| Control | This server | Your responsibility |
|---|---|---|
| No open network port | ✓ stdio only | Keep it stdio; don't wrap in HTTP without adding auth |
| Read-only data access | ✓ queries only | Grant DB/file/Azure credentials with read permissions only |
| No external network calls | ✓ by default | Mount containers without egress unless required |
| No secrets in tool results | ✓ by design | Don't log secrets in your application logs |
| Container isolation | — | Run in a container with read-only volume mounts |
| Version pinning | — | Pin NuGet packages and container digest |
| Tool result sanitisation | — | Review what your logs contain before exposing to an LLM |

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
