#Requires -Version 7.0
<#
.SYNOPSIS
    End-to-end integration test for the AiOps agent.

.DESCRIPTION
    Builds both the agent and mcpserver, then runs the agent in one-shot mode
    against the real Anthropic API.  The agent spawns the mcpserver as a
    subprocess, calls Claude via the Anthropic SDK, dispatches every tool call
    through the MCP client, and writes a JSON result file.

    The script then validates the JSON file and prints a pass/fail summary.

    Prerequisites
    ─────────────
    • $env:ANTHROPIC_API_KEY must be set to a valid Anthropic API key.
    • .NET SDK 10 must be on PATH.
    • Run from the repository root (where AiOps.McpServer.sln lives), or from
      any directory — the script finds the solution root via $PSScriptRoot.

.PARAMETER TimeoutSeconds
    Maximum wall-clock seconds to wait for the agent run.  Default 300 (5 min).

.PARAMETER MaxResults
    Maximum log entries retrieved per repository.  Lower values make the run
    cheaper; default 10.

.PARAMETER TimeRangeHours
    Hours of log history to query.  Default 1.

.PARAMETER MaxTokensPerTurn
    Claude's max_tokens cap per response turn.  Default 4096.

.PARAMETER Model
    Override the Claude model.  Empty string = use the value in appsettings.json.

.PARAMETER SkipBuild
    Skip the dotnet build step (use existing binaries).

.PARAMETER KeepOutput
    Do not delete the temp result directory after the test completes.

.EXAMPLE
    $env:ANTHROPIC_API_KEY = "sk-ant-api03-..."
    .\Test-AgentIntegration.ps1

.EXAMPLE
    .\Test-AgentIntegration.ps1 -SkipBuild -KeepOutput -Verbose

.EXAMPLE
    .\Test-AgentIntegration.ps1 -Model "claude-3-5-haiku-20241022" -MaxTokensPerTurn 2048
#>
[CmdletBinding()]
param(
    [int]    $TimeoutSeconds   = 300,
    [int]    $MaxResults       = 10,
    [double] $TimeRangeHours   = 1,
    [int]    $MaxTokensPerTurn = 4096,
    [string] $Model            = "",
    [switch] $SkipBuild,
    [switch] $KeepOutput
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ── Assertion state ───────────────────────────────────────────────────────────
$script:Passed = 0
$script:Failed = 0

# ── Formatting helpers ────────────────────────────────────────────────────────
function Write-Banner([string]$Title) {
    $line = '═' * 68
    Write-Host "`n$line"            -ForegroundColor DarkCyan
    Write-Host "  $Title"           -ForegroundColor Cyan
    Write-Host "$line`n"            -ForegroundColor DarkCyan
}

function Write-Step([string]$Msg) {
    Write-Host "── $Msg" -ForegroundColor DarkGray
}

function Assert-That([bool]$Condition, [string]$PassMsg, [string]$FailMsg) {
    if ($Condition) {
        Write-Host "  [PASS] $PassMsg" -ForegroundColor Green
        $script:Passed++
    } else {
        Write-Host "  [FAIL] $FailMsg" -ForegroundColor Red
        $script:Failed++
    }
}

function Fail-Fast([string]$Reason) {
    Write-Host "`n  [ABORT] $Reason" -ForegroundColor Red
    exit 1
}

# ── Paths ─────────────────────────────────────────────────────────────────────
$SolutionRoot = $PSScriptRoot
$AgentDir     = Join-Path $SolutionRoot "agent"
$McpDir       = Join-Path $SolutionRoot "mcpserver"
$TempOutput   = Join-Path ([IO.Path]::GetTempPath()) ("aiops-inttest-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$AgentLogFile = Join-Path $TempOutput "_agent-run.log"

# ── Cleanup ───────────────────────────────────────────────────────────────────
function Invoke-Cleanup {
    if ($KeepOutput) { return }
    if (Test-Path $TempOutput) {
        Remove-Item $TempOutput -Recurse -Force -ErrorAction SilentlyContinue
    }
}
# Always clean up, even on Ctrl-C
$null = Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action { Invoke-Cleanup }

# ─────────────────────────────────────────────────────────────────────────────
#  1. PREREQUISITES
# ─────────────────────────────────────────────────────────────────────────────
Write-Banner "1  Prerequisites"

# API key
$apiKey = $env:ANTHROPIC_API_KEY
if (-not $apiKey) {
    Write-Host "  ANTHROPIC_API_KEY is not set.  Export it first:" -ForegroundColor Yellow
    Write-Host "    `$env:ANTHROPIC_API_KEY = 'sk-ant-api03-...'" -ForegroundColor Yellow
    exit 1
}
$maskedKey = $apiKey.Substring(0, [Math]::Min(14, $apiKey.Length)) + "***"
Write-Host "  [OK] ANTHROPIC_API_KEY: $maskedKey" -ForegroundColor Green

# dotnet
try   { $dotnetVer = & dotnet --version 2>&1 | Select-Object -First 1 }
catch { Fail-Fast "dotnet not found on PATH" }
Write-Host "  [OK] dotnet $dotnetVer" -ForegroundColor Green

# Projects
if (-not (Test-Path (Join-Path $AgentDir "AiOps.Agent.csproj"))) {
    Fail-Fast "agent project not found at: $AgentDir"
}
if (-not (Test-Path (Join-Path $McpDir "AiOps.McpServer.csproj"))) {
    Fail-Fast "mcpserver project not found at: $McpDir"
}
Write-Host "  [OK] Projects found" -ForegroundColor Green

# ─────────────────────────────────────────────────────────────────────────────
#  2. BUILD
# ─────────────────────────────────────────────────────────────────────────────
Write-Banner "2  Build"

if ($SkipBuild) {
    Write-Host "  Skipping build (-SkipBuild)" -ForegroundColor Yellow
} else {
    foreach ($proj in @("mcpserver\AiOps.McpServer.csproj", "agent\AiOps.Agent.csproj")) {
        $projPath = Join-Path $SolutionRoot $proj
        Write-Step "dotnet build $proj"
        & dotnet build $projPath -c Debug --verbosity minimal 2>&1 |
            ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -ne 0) { Fail-Fast "Build failed: $proj" }
        Write-Host "  [OK] $proj" -ForegroundColor Green
    }
}

# ─────────────────────────────────────────────────────────────────────────────
#  3. RUN AGENT (one-shot)
# ─────────────────────────────────────────────────────────────────────────────
Write-Banner "3  Running agent (one-shot mode)"

New-Item -ItemType Directory -Force -Path $TempOutput | Out-Null

Write-Host "  OutputDirectory: $TempOutput"
Write-Host "  MaxResults:      $MaxResults"
Write-Host "  TimeRangeHours:  $TimeRangeHours"
Write-Host "  MaxTokens/turn:  $MaxTokensPerTurn"
if ($Model) { Write-Host "  Model:           $Model" }
Write-Host ""

# ── Configure via environment variables ───────────────────────────────────────
#
# The Generic Host maps  Agent__Foo  →  Agent:Foo  (double-underscore = colon).
#
# NOTE: We do NOT override Agent__McpServerArguments__* here.
# The Generic Host MERGES (not replaces) array config from environment variables
# with values already present in appsettings.json.  Overriding individual array
# indices would produce a doubled array, e.g.:
#
#   appsettings:  run --project ../mcpserver --no-build
#   env vars:     run --project ../mcpserver --no-build          ← duplicated!
#
# Instead we launch the agent job with its working directory set to $AgentDir,
# so the default relative path "../mcpserver" in appsettings.json resolves
# correctly without any env-var array overrides.
#
$envOverrides = [ordered]@{
    # One-shot: run once, write JSON, exit
    "Agent__IntervalMinutes"       = "0"

    # Absolute output path so we can find the result file
    "Agent__OutputDirectory"       = $TempOutput

    # Keep the run small to reduce cost and latency
    "Agent__MaxResults"            = "$MaxResults"
    "Agent__TimeRangeHours"        = "$TimeRangeHours"
    "Agent__MaxTokensPerTurn"      = "$MaxTokensPerTurn"
}

if ($Model) { $envOverrides["Agent__Model"] = $Model }

# Save current values, then apply overrides
$savedEnv = @{}
foreach ($kv in $envOverrides.GetEnumerator()) {
    $savedEnv[$kv.Key] = [System.Environment]::GetEnvironmentVariable($kv.Key)
    [System.Environment]::SetEnvironmentVariable($kv.Key, $kv.Value)
}

# ── Launch the agent as a background job (streams live output) ───────────────
Write-Step "Starting agent process…"
Write-Host ""

$capturedLines = [System.Collections.Generic.List[string]]::new()
$agentExitCode = -1

$agentJob = Start-Job -Name "AiOpsAgentRun" -WorkingDirectory $AgentDir -ScriptBlock {
    # Run from $AgentDir so that the appsettings relative path "../mcpserver"
    # resolves correctly.  Start-Job inherits the parent's process environment,
    # so all Agent__* overrides set above are visible to the agent.
    # *>&1 merges all PS streams so output is visible via Receive-Job.
    & dotnet run --no-build *>&1
    # Return the exit code as the last output item (a boxed int)
    [int]$LASTEXITCODE
}

$timer   = [System.Diagnostics.Stopwatch]::StartNew()
$prefix  = "  │  "
$timedOut = $false

try {
    while ($agentJob.State -notin @('Completed', 'Failed', 'Stopped')) {
        if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
            Write-Host "`n  [TIMEOUT] Killing job after $TimeoutSeconds s…" -ForegroundColor Red
            Stop-Job $agentJob
            $timedOut = $true
            break
        }

        $batch = Receive-Job $agentJob 2>$null
        foreach ($item in $batch) {
            $line = "$item"
            $capturedLines.Add($line)
            Write-Host "$prefix$line"
        }
        Start-Sleep -Milliseconds 300
    }

    # Drain any remaining output
    Start-Sleep -Milliseconds 500
    $batch = Receive-Job $agentJob 2>$null
    foreach ($item in $batch) {
        $line = "$item"
        $capturedLines.Add($line)
        Write-Host "$prefix$line"
    }

    # The last item from the job is the exit code integer
    if ($capturedLines.Count -gt 0) {
        $lastItem = $capturedLines[-1]
        if ($lastItem -match '^\-?\d+$') {
            $agentExitCode = [int]$lastItem
            $capturedLines.RemoveAt($capturedLines.Count - 1)
        }
    }
} finally {
    # Restore original env vars
    foreach ($kv in $savedEnv.GetEnumerator()) {
        [System.Environment]::SetEnvironmentVariable($kv.Key, $kv.Value)
    }
    Remove-Job $agentJob -Force -ErrorAction SilentlyContinue
}

$elapsed = [Math]::Round($timer.Elapsed.TotalSeconds, 1)
Write-Host ""
Write-Host "  Agent finished in ${elapsed}s  (exit code: $agentExitCode)" -ForegroundColor DarkGray

# Save full output to log
$capturedLines | Set-Content $AgentLogFile -Encoding UTF8

# ─────────────────────────────────────────────────────────────────────────────
#  4. ASSERTIONS
# ─────────────────────────────────────────────────────────────────────────────
Write-Banner "4  Assertions"

# ── Process / timing ──────────────────────────────────────────────────────────
Assert-That (-not $timedOut) `
    "Agent completed within ${TimeoutSeconds}s (took ${elapsed}s)" `
    "Agent timed out after ${TimeoutSeconds}s — check $AgentLogFile"

Assert-That ($agentExitCode -eq 0) `
    "Agent process exited cleanly (code 0)" `
    "Agent exited with code $agentExitCode"

# ── Output file ───────────────────────────────────────────────────────────────
$jsonFiles = @(Get-ChildItem $TempOutput -Filter "analysis_*.json" -File -ErrorAction SilentlyContinue)

Assert-That ($jsonFiles.Count -ge 1) `
    "Result JSON file written to $TempOutput" `
    "No analysis_*.json file found in $TempOutput"

Assert-That ($jsonFiles.Count -eq 1) `
    "Exactly one result file created" `
    "Expected 1 result file, found $($jsonFiles.Count)"

# Filename format: analysis_YYYYMMDD_HHmmss_xxxxxxxx.json
if ($jsonFiles.Count -ge 1) {
    $fname = $jsonFiles[0].Name
    Assert-That ($fname -match '^analysis_\d{8}_\d{6}_[0-9a-f]{8}\.json$') `
        "File name matches expected format  ($fname)" `
        "File name does not match 'analysis_YYYYMMDD_HHmmss_xxxxxxxx.json':  $fname"
}

# ── Parse the JSON ────────────────────────────────────────────────────────────
$run = $null
if ($jsonFiles.Count -ge 1) {
    try {
        $run = Get-Content $jsonFiles[0].FullName -Raw | ConvertFrom-Json
        Assert-That ($null -ne $run) "Result JSON parsed successfully" "JSON parse error"
    } catch {
        Assert-That $false "Result JSON parsed successfully" "JSON parse error: $_"
    }
}

if ($null -ne $run) {

    # ── Run metadata ──────────────────────────────────────────────────────────
    Assert-That ($run.runId -match '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') `
        "runId is a valid GUID  ($($run.runId))" `
        "runId is not a valid GUID:  $($run.runId)"

    Assert-That ($null -ne $run.startedAt) `
        "startedAt is set  ($($run.startedAt))" `
        "startedAt is null"

    Assert-That ($null -ne $run.completedAt) `
        "completedAt is set  ($($run.completedAt))" `
        "completedAt is null"

    $startedAt   = [DateTimeOffset]::Parse($run.startedAt)
    $completedAt = [DateTimeOffset]::Parse($run.completedAt)
    Assert-That ($completedAt -ge $startedAt) `
        "completedAt ($([Math]::Round(($completedAt - $startedAt).TotalSeconds, 1))s after startedAt)" `
        "completedAt is before startedAt"

    Assert-That (-not [string]::IsNullOrWhiteSpace($run.model)) `
        "model is set  ($($run.model))" `
        "model field is empty"

    # ── Success ───────────────────────────────────────────────────────────────
    if ($run.success -eq $true) {
        Assert-That $true "Run reported success=true" "unreachable"
    } else {
        Assert-That $false `
            "unreachable" `
            "Run failed — errorType: $($run.errorType) | $($run.errorMessage)"
    }

    # ── LLM was called ────────────────────────────────────────────────────────
    $inTok  = [long]$run.inputTokens
    $outTok = [long]$run.outputTokens

    Assert-That ($inTok -gt 0) `
        "Claude consumed input tokens  ($inTok)" `
        "inputTokens = 0 — Anthropic API may not have been reached"

    Assert-That ($outTok -gt 0) `
        "Claude produced output tokens  ($outTok)" `
        "outputTokens = 0 — Claude may have produced no response"

    # ── MCP tools were called ─────────────────────────────────────────────────
    # Always wrap pipeline results in @() so that Set-StrictMode -Version Latest
    # does not throw when the pipeline produces zero items (result would be $null,
    # and $null.Count is not accessible under strict mode).
    $toolCalls    = @($run.toolCalls)          # guarantees an array, even when empty
    $toolCount    = $toolCalls.Count

    Assert-That ($toolCount -ge 1) `
        "At least one MCP tool call was made  ($toolCount total)" `
        "toolCalls is empty — the mcpserver connection may have failed"

    $listCalled = @($toolCalls | Where-Object { $_.toolName -eq "list_log_repositories" }).Count -gt 0
    Assert-That $listCalled `
        "list_log_repositories was called (discovers available repos)" `
        "list_log_repositories was never called — agent prompt may not have been followed"

    # Every tool call should have a recorded duration
    $zeroDuration = @($toolCalls | Where-Object { [long]$_.durationMs -lt 0 }).Count
    Assert-That ($zeroDuration -eq 0) `
        "All tool calls have non-negative durationMs" `
        "$zeroDuration tool calls have negative durationMs (timing bug)"

    # ── Final report ──────────────────────────────────────────────────────────
    Assert-That (-not [string]::IsNullOrWhiteSpace($run.finalReport)) `
        "finalReport is non-empty" `
        "finalReport is null/empty — Claude never produced a consolidated report"

    if (-not [string]::IsNullOrWhiteSpace($run.finalReport)) {
        Assert-That ($run.finalReport.Length -gt 100) `
            "finalReport has meaningful length  ($($run.finalReport.Length) chars)" `
            "finalReport is suspiciously short  ($($run.finalReport.Length) chars)"

        Assert-That ($run.finalReport -match '#') `
            "finalReport contains Markdown headings" `
            "finalReport has no Markdown headings (unexpected format)"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
#  5. SUMMARY
# ─────────────────────────────────────────────────────────────────────────────
Write-Banner "5  Summary"

if ($null -ne $run) {
    $duration = if ($null -ne $run.completedAt -and $null -ne $run.startedAt) {
        $s = [DateTimeOffset]::Parse($run.startedAt)
        $e = [DateTimeOffset]::Parse($run.completedAt)
        "$([Math]::Round(($e - $s).TotalSeconds, 1))s"
    } else { "n/a" }

    Write-Host "  Run ID     : $($run.runId)"
    Write-Host "  Model      : $($run.model)"
    Write-Host "  Duration   : $duration  (wall clock: ${elapsed}s)"
    Write-Host "  Tokens     : $($run.inputTokens) in  /  $($run.outputTokens) out"
    $summaryToolCalls = @($run.toolCalls)     # always an array (StrictMode-safe)
    Write-Host "  Tool calls : $($summaryToolCalls.Count)"

    if ($summaryToolCalls.Count -gt 0) {
        Write-Host ""
        Write-Host ("  ┌─ Tool call log " + ('─' * 48))
        foreach ($tc in $summaryToolCalls) {
            $badge = if ($tc.isError) { " ERR " } else { " ok  " }
            $color = if ($tc.isError) { "Yellow" } else { "DarkGray" }
            Write-Host ("  │ [$badge] {0,-35} {1,5}ms" -f $tc.toolName, $tc.durationMs) -ForegroundColor $color
        }
        Write-Host ("  └" + ('─' * 62))
    }

    if (-not [string]::IsNullOrWhiteSpace($run.finalReport)) {
        Write-Host ""
        Write-Host ("  ┌─ Final report (first 15 lines) " + ('─' * 32))
        $run.finalReport -split "`n" |
            Select-Object -First 15 |
            ForEach-Object { Write-Host "  │  $_" -ForegroundColor Gray }
        $totalLines = ($run.finalReport -split "`n").Count
        if ($totalLines -gt 15) {
            Write-Host "  │  … ($($totalLines - 15) more lines)" -ForegroundColor DarkGray
        }
        Write-Host ("  └" + ('─' * 62))
    }
}

# ── Overall result ────────────────────────────────────────────────────────────
Write-Host ""
$resultColor = if ($script:Failed -eq 0) { "Green" } else { "Red" }
$resultText  = if ($script:Failed -eq 0) { "ALL TESTS PASSED" } else { "$($script:Failed) TEST(S) FAILED" }
Write-Host ("  {0} passed   {1} failed   —   {2}" -f $script:Passed, $script:Failed, $resultText) `
    -ForegroundColor $resultColor

Write-Host ""
if ($KeepOutput) {
    Write-Host "  Result directory retained: $TempOutput" -ForegroundColor Yellow
    if ($script:Passed -gt 0 -and $jsonFiles.Count -gt 0) {
        Write-Host "  Result JSON:               $($jsonFiles[0].FullName)" -ForegroundColor Yellow
    }
    Write-Host "  Agent log:                 $AgentLogFile" -ForegroundColor Yellow
} else {
    Invoke-Cleanup
    Write-Host "  Temp directory cleaned up  (use -KeepOutput to retain it)" -ForegroundColor DarkGray
}

# Exit with non-zero if any assertion failed
exit $(if ($script:Failed -gt 0) { 1 } else { 0 })
