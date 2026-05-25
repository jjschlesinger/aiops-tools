# AiOps

A collection of independent AI-assisted operations tooling. Each sub-folder is a self-contained project with its own build system, dependencies, and configuration.

---

## Projects

### [`mcpserver/`](mcpserver/)

**AiOps MCP Server** — a [Model Context Protocol](https://modelcontextprotocol.io) server written in **.NET 10** that lets an LLM query structured log repositories, find errors and exceptions, and generate Markdown analysis reports with fix recommendations.

| | |
|---|---|
| **Runtime** | .NET 10 |
| **Transport** | stdio (MCP standard) |
| **NuGet** | `ModelContextProtocol`, `Azure.Monitor.Query`, `Dapper` |

#### Tools exposed

| Tool | Description |
|---|---|
| `list_log_repositories` | Discover all configured repositories |
| `query_log_errors` | Query a repository and return structured JSON for interactive exploration |
| `generate_analysis_report` | Query a repository and produce a full Markdown report with timelines, grouped stack traces, investigation hints, and a fix-recommendation template |

#### Log repository implementations

| Implementation | Config `Type` | Backend |
|---|---|---|
| `SerilogFileLogRepository` | `SerilogFile` | Serilog Compact Log Event Format (CLEF) files — one JSON object per line |
| `SqlLogRepository` | `Sql` | Any ADO.NET-compatible database via `DbConnection` + `DbProviderFactory` (SQL Server, PostgreSQL, MySQL, SQLite) |
| `AzureMonitorLogRepository` | `AzureMonitor` | Azure Monitor Log Analytics workspace via Kusto — supports `AppExceptions` and `AppTraces` tables |

#### Quick start

```bash
cd mcpserver

# restore packages (update ModelContextProtocol version if needed)
dotnet restore

# run the server (communicates over stdin/stdout)
dotnet run
```

Configure repositories in `mcpserver/appsettings.json` before running. See the file for annotated examples for each backend type.

---

## Repository layout

```
aiops/
├── README.md
├── .gitignore          # shared ignore rules for all projects
└── mcpserver/          # .NET 10 MCP server (see above)
```

---

## Adding a new project

Each project lives in its own sub-folder and is fully independent — no shared solution file or cross-project references. To add one:

1. Create a sub-folder: `mkdir myproject`
2. Scaffold the project inside it (`dotnet new`, `npm init`, etc.)
3. Add a section to this README following the pattern above.
