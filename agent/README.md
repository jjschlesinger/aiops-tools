# AiOps Agent

A **.NET 10** console application that runs Claude as an autonomous log-analysis agent. It spawns the [AiOps MCP Server](../mcpserver/README.md) as a subprocess, drives a multi-turn tool-use loop, and writes a structured JSON result file after each run.

---

## How it works

1. **Spawn** — the agent starts the MCP server as a stdio subprocess using the command configured in `appsettings.json`
2. **Discover** — calls `ListToolsAsync` to retrieve all tool schemas from the MCP server
3. **Prompt** — sends a log-analysis prompt to Claude together with the tool schemas
4. **Loop** — for every `tool_use` block in Claude's response, dispatches the call to the MCP server and feeds the result back as the next user turn
5. **Finish** — when Claude returns `end_turn` with no pending tool calls, the loop exits
6. **Write** — serialises the full run (metadata, every tool call record, final Markdown report) to `analysis_YYYYMMDD_HHmmss_<runId>.json`

---

## Modes

| `IntervalMinutes` | Behaviour |
|---|---|
| `> 0` | **Periodic** — re-runs on the configured schedule (e.g. `60` = hourly) |
| `≤ 0` | **One-shot** — runs once on startup, then exits. Useful in CI pipelines and scripts |

---

## Quick start

### Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- A valid [Anthropic API key](https://console.anthropic.com/)
- The `mcpserver` project built (or let the agent build it on first run — remove `--no-build` from `McpServerArguments`)

### Run

```bash
# Set your API key (Windows PowerShell)
$env:ANTHROPIC_API_KEY = "sk-ant-api03-..."

# Or on Linux / macOS
export ANTHROPIC_API_KEY="sk-ant-api03-..."

cd agent
dotnet restore
dotnet run
```

Results are written to the `results/` directory relative to the agent executable.

### Run in one-shot mode (CI / scripts)

Set `IntervalMinutes` to `0` in `appsettings.json`, or pass it as an override:

```bash
dotnet run -- --Agent:IntervalMinutes=0
```

---

## Configuration

All settings live in `agent/appsettings.json`:

```jsonc
{
  "Agent": {
    // Command used to launch the MCP server subprocess.
    // Dev: "dotnet" with the args below runs the mcpserver project in-place.
    // Prod: set to the path of the published mcpserver binary.
    "McpServerCommand": "dotnet",
    "McpServerArguments": [ "run", "--project", "../mcpserver", "--no-build" ],

    // Claude model. See https://docs.anthropic.com/en/docs/about-claude/models
    "Model": "claude-opus-4-5-20250929",

    // How many hours of logs to analyse on each run.
    "TimeRangeHours": 24,

    // Maximum log entries fetched per repository.
    "MaxResults": 200,

    // Minutes between runs. 0 = one-shot mode.
    "IntervalMinutes": 60,

    // Output directory for per-run JSON files (relative to the agent exe).
    "OutputDirectory": "results",

    // Claude's max_tokens per response turn.
    "MaxTokensPerTurn": 8192
  }
}
```

### API key

Supply the Anthropic API key via environment variable — do not put it in `appsettings.json`:

```bash
# Windows
$env:ANTHROPIC_API_KEY = "sk-ant-api03-..."

# Linux / macOS
export ANTHROPIC_API_KEY="sk-ant-api03-..."
```

### Pointing at a published MCP server

In production, publish the MCP server and point the agent at the binary:

```jsonc
"McpServerCommand": "C:\\deploy\\aiops-mcpserver\\AiOps.McpServer.exe",
"McpServerArguments": []
```

---

## Result file format

Each run produces `results/analysis_YYYYMMDD_HHmmss_<runId>.json`:

```jsonc
{
  "RunId": "...",
  "StartedAt": "2025-10-01T08:00:00Z",
  "CompletedAt": "2025-10-01T08:00:45Z",
  "Model": "claude-opus-4-5-20250929",
  "InputTokens": 4200,
  "OutputTokens": 1800,
  "ToolCalls": [
    {
      "ToolName": "list_log_repositories",
      "Input": {},
      "Output": "...",
      "DurationMs": 32
    },
    {
      "ToolName": "generate_analysis_report",
      "Input": { "repositoryName": "prod-serilog", "timeRangeHours": 24 },
      "Output": "...",
      "DurationMs": 210
    }
  ],
  "Report": "# Log Analysis Report\n\n..."
}
```

---

## Project structure

```
agent/
├── AiOps.Agent.csproj
├── appsettings.json                    # agent + MCP server configuration
├── Program.cs                          # host setup, DI wiring
│
├── Configuration/
│   └── AgentConfig.cs                  # strongly-typed options bound from "Agent" section
│
├── Models/
│   ├── AnalysisRun.cs                  # per-run result model serialised to JSON
│   └── ToolCallRecord.cs               # individual tool call (name, input, output, duration)
│
└── Services/
    ├── IAgentOrchestrator.cs           # orchestrator interface
    ├── AgentOrchestrator.cs            # MCP spawn + Claude tool-use loop
    └── LogAnalysisBackgroundService.cs # IHostedService — one-shot or periodic scheduling
```

---

## Dependencies

| Package | Purpose |
|---|---|
| `Anthropic` SDK | Claude API client (multi-turn messages, tool use) |
| `ModelContextProtocol` | MCP client — spawns server, lists tools, dispatches calls |
| `Microsoft.Extensions.Hosting` | Generic Host, DI, configuration, `IHostedService` |

---

## Related

- [`mcpserver/`](../mcpserver/README.md) — the MCP server this agent drives
- [`agent.tests/`](../agent.tests/) — 72-test xUnit suite (fully mocked, no network)
- [`Test-AgentIntegration.ps1`](../Test-AgentIntegration.ps1) — PowerShell end-to-end integration test against the real API
