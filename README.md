# AiOps

AI-assisted operations tooling. The repo contains an MCP log-analysis server, a Claude-powered agent that drives it, and the tests for both.

---

## Projects

### [`mcpserver/`](mcpserver/)

**AiOps MCP Server** — a [Model Context Protocol](https://modelcontextprotocol.io) server written in **.NET 10** that exposes structured log-querying tools over stdio. An LLM (or any MCP client) connects to it, calls the tools to retrieve errors and exceptions from configured repositories, and generates Markdown analysis reports with fix recommendations.

| | |
|---|---|
| **Runtime** | .NET 10 |
| **Transport** | stdio (MCP standard) |
| **Key packages** | `ModelContextProtocol`, `Azure.Monitor.Query`, `Dapper` |

#### Tools exposed

| Tool | Description |
|---|---|
| `list_log_repositories` | Discover all configured repositories |
| `query_log_errors` | Query a repository and return structured JSON for interactive exploration |
| `generate_analysis_report` | Query a repository and produce a full Markdown report with timelines, grouped stack traces, investigation hints, and a fix-recommendation template |

#### Log repository backends

| Implementation | Config `Type` | Backend |
|---|---|---|
| `SerilogFileLogRepository` | `SerilogFile` | Serilog Compact Log Event Format (CLEF) files — one JSON object per line |
| `SqlLogRepository` | `Sql` | Any ADO.NET-compatible database via `DbConnection` + `DbProviderFactory` (SQL Server, PostgreSQL, MySQL, SQLite) |
| `AzureMonitorLogRepository` | `AzureMonitor` | Azure Monitor Log Analytics workspace via Kusto — supports `AppExceptions` and `AppTraces` tables |

#### Quick start

```bash
cd mcpserver
dotnet restore
dotnet run          # communicates over stdin/stdout
```

Configure repositories in `mcpserver/appsettings.json` before running.

---

### [`agent/`](agent/)

**AiOps Agent** — a **.NET 10** console app that runs Claude (via the [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp)) as an autonomous log-analysis agent. It spawns the `mcpserver` as a subprocess, drives an agentic tool-use loop, and writes a structured JSON result file after each run.

| | |
|---|---|
| **Runtime** | .NET 10 |
| **LLM** | Claude (configurable model, default `claude-opus-4-5-20250929`) |
| **Key packages** | `Anthropic` SDK, `ModelContextProtocol`, `Microsoft.Extensions.Hosting` |

#### How it works

1. Spawns the MCP server as a stdio subprocess
2. Discovers available tools via `ListToolsAsync`
3. Sends a log-analysis prompt to Claude together with the tool schemas
4. Dispatches every `tool_use` block Claude returns back to the MCP server
5. Feeds results into the next Claude turn until the model stops requesting tools
6. Writes an `analysis_YYYYMMDD_HHmmss_<runId>.json` result file containing the run metadata, every tool call record, and the final Markdown report

#### Modes

| `IntervalMinutes` | Behaviour |
|---|---|
| `> 0` | Periodic — reruns on the configured schedule |
| `≤ 0` | One-shot — runs once on startup then exits (useful for CI / scripts) |

#### Quick start

```bash
cd agent
# set your Anthropic API key
export ANTHROPIC_API_KEY=sk-ant-api03-...   # or $env:ANTHROPIC_API_KEY on Windows

dotnet restore
dotnet run
```

Results are written to the `results/` directory by default. Configure the agent in `agent/appsettings.json`.

---

### [`agent.tests/`](agent.tests/)

**AiOps Agent Unit Tests** — an xUnit test project (72 tests) that covers the agent project in isolation. The Anthropic + MCP infrastructure is replaced by mocks so the tests run without any network calls or live processes.

| | |
|---|---|
| **Framework** | xUnit 2.9, FluentAssertions 6, Moq 4 |
| **Coverage** | `AgentConfig`, `AnalysisRun` / `ToolCallRecord`, `LogAnalysisBackgroundService`, `AgentOrchestrator` failure paths, internal helpers (`BuildToolArgs`, `ExtractText`) |

```bash
cd agent.tests
dotnet test
```

---

### [`mcpserver.tests/`](mcpserver.tests/)  *(if present)*

xUnit tests for the MCP server tools and log-repository implementations.

---

## Integration test

[`Test-AgentIntegration.ps1`](Test-AgentIntegration.ps1) runs a full end-to-end test against the real Anthropic API:

1. Builds both `mcpserver` and `agent`
2. Runs the agent in one-shot mode
3. Asserts ~18 properties of the resulting JSON file (run ID, timestamps, model, token counts, tool calls made, `list_log_repositories` invoked, final report structure)

**Prerequisites:** PowerShell 7+, .NET SDK 10, a valid `ANTHROPIC_API_KEY`.

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-api03-..."
.\Test-AgentIntegration.ps1

# Common flags
.\Test-AgentIntegration.ps1 -SkipBuild -KeepOutput -TimeoutSeconds 120
.\Test-AgentIntegration.ps1 -Model "claude-3-5-haiku-20241022" -MaxTokensPerTurn 2048
```

---

## Repository layout

```
aiops/
├── README.md
├── AiOps.McpServer.sln             # solution — includes all four projects
├── Test-AgentIntegration.ps1       # PowerShell end-to-end integration test
├── .gitignore
├── mcpserver/                      # .NET 10 MCP server
│   ├── AiOps.McpServer.csproj
│   └── appsettings.json
├── mcpserver.tests/                # xUnit tests for the MCP server
│   └── AiOps.McpServer.Tests.csproj
├── agent/                          # .NET 10 Claude agent
│   ├── AiOps.Agent.csproj
│   └── appsettings.json
└── agent.tests/                    # xUnit tests for the agent (72 tests)
    └── AiOps.Agent.Tests.csproj
```
