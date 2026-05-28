using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using AiOps.Agent.Configuration;
using AiOps.Agent.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Transport;
using McpTool = ModelContextProtocol.Protocol.Types.Tool;
using McpCallResult = ModelContextProtocol.Protocol.Types.CallToolResponse;

namespace AiOps.Agent.Services;

/// <summary>
/// Orchestrates a single analysis run:
/// 1. Launches the MCP server as a subprocess via stdio
/// 2. Lists available tools from the MCP server
/// 3. Runs Claude in an agentic loop, dispatching every tool call through the MCP client
/// 4. Returns a completed <see cref="AnalysisRun"/> ready to be serialised to disk
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly AgentConfig _config;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly AnthropicClient _anthropicClient;

    /// <param name="anthropicClient">
    /// Optional pre-built client; when <see langword="null"/> a default client is
    /// created that reads <c>ANTHROPIC_API_KEY</c> from the environment.
    /// Pass an explicit instance in tests to avoid touching real infrastructure.
    /// </param>
    public AgentOrchestrator(
        AgentConfig config,
        ILogger<AgentOrchestrator> logger,
        AnthropicClient? anthropicClient = null)
    {
        _config = config;
        _logger = logger;
        _anthropicClient = anthropicClient ?? new AnthropicClient();
    }

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<AnalysisRun> RunAsync(CancellationToken cancellationToken = default)
    {
        var run = new AnalysisRun
        {
            StartedAt = DateTimeOffset.UtcNow,
            Model = _config.Model,
        };

        try
        {
            await ExecuteRunAsync(run, cancellationToken);
            run.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis run {RunId} failed", run.RunId);
            run.Success = false;
            run.ErrorType = ex.GetType().Name;
            run.ErrorMessage = ex.Message;
        }
        finally
        {
            run.CompletedAt = DateTimeOffset.UtcNow;
        }

        return run;
    }

    // ── Core execution ────────────────────────────────────────────────────────

    private async Task ExecuteRunAsync(AnalysisRun run, CancellationToken ct)
    {
        // ── 1. Launch the MCP server and connect ─────────────────────────────
        var serverConfig = new McpServerConfig
        {
            Id            = "aiops-mcpserver",
            Name          = "AiOps MCP Server",
            TransportType = "stdio",
            Location      = _config.McpServerCommand,
            Arguments     = _config.McpServerArguments, // string[]
        };

        _logger.LogInformation("Starting MCP server: {Cmd} {Args}",
            _config.McpServerCommand, string.Join(" ", _config.McpServerArguments));

        // Provide an explicit stdio factory so the Arguments array is
        // correctly joined into the single-string form that StdioClientTransportOptions expects.
        await using var mcpClient = await McpClientFactory.CreateAsync(
            serverConfig,
            new McpClientOptions
            {
                ClientInfo = new ModelContextProtocol.Protocol.Types.Implementation
                {
                    Name    = "AiOps.Agent",
                    Version = "1.0.0",
                },
            },
            CreateStdioTransport,
            null,
            ct);

        // ── 2. Discover tools ─────────────────────────────────────────────────
        // ListToolsAsync returns IAsyncEnumerable<McpTool>
        var mcpTools = new List<McpTool>();
        await foreach (var tool in mcpClient.ListToolsAsync(ct))
            mcpTools.Add(tool);

        _logger.LogInformation("Discovered {Count} MCP tools: {Names}",
            mcpTools.Count,
            string.Join(", ", mcpTools.Select(t => t.Name)));

        var anthropicTools = mcpTools
            .Select(t => ToAnthropicTool(t))
            .ToList<ToolUnion>();

        // ── 3. Prime the conversation ─────────────────────────────────────────
        _logger.LogInformation("Starting agentic loop with model {Model}", _config.Model);

        // Use string literals for role to avoid ambiguity with MCP's Role type.
        var history = new List<MessageParam>
        {
            new() { Role = "user", Content = BuildUserPrompt() },
        };

        // ── 4. Agentic loop ───────────────────────────────────────────────────
        var textAccumulator = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var response = await _anthropicClient.Messages.Create(
                new MessageCreateParams
                {
                    Model     = _config.Model,
                    MaxTokens = _config.MaxTokensPerTurn,
                    System    = BuildSystemPrompt(),
                    Messages  = history,
                    Tools     = anthropicTools,
                },
                cancellationToken: ct);

            run.InputTokens  += response.Usage.InputTokens;
            run.OutputTokens += response.Usage.OutputTokens;

            // Collect text the model emitted this turn
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var textBlock))
                    textAccumulator.AppendLine(textBlock.Text);
            }

            // Raw() returns the underlying string value (e.g. "tool_use"); Value() returns the enum.
            var stopReason = response.StopReason?.Raw() ?? "";

            _logger.LogDebug("Stop reason: {Reason}", stopReason);

            if (stopReason != "tool_use")
            {
                _logger.LogInformation("Claude finished (stop_reason={Reason})", stopReason);
                break;
            }

            // ── 4a. Echo assistant turn back into history ──────────────────────
            var assistantContent = response.Content
                .Select(b => BlockToParam(b))
                .ToList();  // List<ContentBlockParam> — implicit conversion to MessageParamContent

            history.Add(new MessageParam
            {
                Role    = "assistant",
                Content = assistantContent,
            });

            // ── 4b. Execute every tool call ───────────────────────────────────
            var toolResults = new List<ContentBlockParam>();

            foreach (var block in response.Content)
            {
                if (!block.TryPickToolUse(out var toolUse))
                    continue;

                // toolUse.Input is IReadOnlyDictionary<string, JsonElement>
                var inputJson = JsonSerializer.Serialize(toolUse.Input);
                _logger.LogInformation("→ Tool: {Tool}({Input})", toolUse.Name, inputJson);

                var record = new ToolCallRecord
                {
                    ToolName = toolUse.Name,
                    Input    = inputJson,
                    CalledAt = DateTimeOffset.UtcNow,
                };

                var sw = Stopwatch.StartNew();
                string resultText;
                try
                {
                    var args       = BuildToolArgs(toolUse.Input);
                    var callResult = await mcpClient.CallToolAsync(toolUse.Name, args, ct);
                    resultText     = ExtractText(callResult);
                    record.IsError = callResult.IsError;
                }
                catch (Exception ex)
                {
                    resultText     = $"Error: {ex.Message}";
                    record.IsError = true;
                    _logger.LogWarning(ex, "MCP tool '{Tool}' threw", toolUse.Name);
                }

                sw.Stop();
                record.Output     = resultText;
                record.DurationMs = sw.ElapsedMilliseconds;
                run.ToolCalls.Add(record);

                _logger.LogInformation("← {Tool} in {Ms}ms (isError={Err})",
                    toolUse.Name, record.DurationMs, record.IsError);

                toolResults.Add(new ToolResultBlockParam
                {
                    ToolUseID = toolUse.ID,
                    Content   = resultText,
                });
            }

            // ── 4c. Send tool results back to Claude ───────────────────────────
            history.Add(new MessageParam
            {
                Role    = "user",
                Content = toolResults,  // List<ContentBlockParam> via implicit MessageParamContent conversion
            });
        }

        run.FinalReport = textAccumulator.ToString().Trim();
    }

    // ── Transport factory ─────────────────────────────────────────────────────

    private static IClientTransport CreateStdioTransport(
        McpServerConfig cfg,
        Microsoft.Extensions.Logging.ILoggerFactory? logFactory)
    {
        // StdioClientTransportOptions.Arguments is a single string; join the array.
        var args = cfg.Arguments is { Length: > 0 }
            ? string.Join(" ", cfg.Arguments)
            : null;

        return new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command   = cfg.Location ?? "",
                Arguments = args,
            },
            cfg,
            logFactory);
    }

    // ── Tool schema conversion ─────────────────────────────────────────────────

    internal static ToolUnion ToAnthropicTool(McpTool mcp)
    {
        var schemaEl   = mcp.InputSchema;
        var schemaDict = schemaEl.ValueKind == JsonValueKind.Object
            ? schemaEl.Deserialize<Dictionary<string, JsonElement>>()
              ?? new Dictionary<string, JsonElement>()
            : new Dictionary<string, JsonElement>();

        return new Anthropic.Models.Messages.Tool
        {
            Name        = mcp.Name,
            Description = mcp.Description ?? "",
            InputSchema = InputSchema.FromRawUnchecked(schemaDict),
        };
    }

    // ── Block conversion (response → history param) ────────────────────────────

    internal static ContentBlockParam BlockToParam(ContentBlock block)
    {
        if (block.TryPickText(out var text))
            return new TextBlockParam { Text = text.Text };

        if (block.TryPickToolUse(out var toolUse))
            return new ToolUseBlockParam
            {
                ID    = toolUse.ID,
                Name  = toolUse.Name,
                Input = toolUse.Input, // IReadOnlyDictionary<string, JsonElement>
            };

        // Thinking / redacted-thinking / other blocks — keep history intact.
        return new TextBlockParam { Text = "[non-text block]" };
    }

    // ── Argument / result helpers ──────────────────────────────────────────────

    /// <summary>
    /// Convert the tool-use input (IReadOnlyDictionary) into the non-nullable
    /// <c>Dictionary&lt;string, object&gt;</c> expected by <c>CallToolAsync</c>.
    /// </summary>
    internal static Dictionary<string, object> BuildToolArgs(
        IReadOnlyDictionary<string, JsonElement> input)
    {
        var result = new Dictionary<string, object>(input.Count, StringComparer.Ordinal);
        foreach (var (key, value) in input)
        {
            result[key] = value.ValueKind switch
            {
                JsonValueKind.String => (object)(value.GetString() ?? ""),
                JsonValueKind.Number => value.TryGetInt64(out var l)
                    ? (object)l
                    : value.GetDouble(),
                JsonValueKind.True   => true,
                JsonValueKind.False  => false,
                JsonValueKind.Null   => "",  // CallToolAsync expects non-nullable
                _                    => value.GetRawText(),
            };
        }
        return result;
    }

    internal static string ExtractText(McpCallResult result)
    {
        if (result.Content is null or { Count: 0 })
            return "(no output)";

        return string.Join("\n", result.Content
            .Select(c => c.Text ?? c.Type ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    // ── Prompts ───────────────────────────────────────────────────────────────

    private string BuildSystemPrompt() =>
        """
        You are an expert SRE / platform engineer specialising in log analysis and incident triage.

        Your task is to autonomously analyse all configured log repositories for recent errors and
        produce a comprehensive Markdown report that covers:
        - An executive summary with exception frequency table
        - Per-exception-type detail sections including representative stack traces
        - An error timeline
        - Recommended investigation areas and next steps
        - An "Analysis & Recommended Fixes" section with concrete, actionable suggestions

        Guidelines:
        - Use the available tools to discover repositories, retrieve errors, and generate reports.
        - Prefer the generate_analysis_report tool for the final per-repository report.
        - Analyse every repository listed, then combine findings into a single report.
        - Always end with one consolidated Markdown report that synthesises all findings.
        """;

    private string BuildUserPrompt() =>
        $"""
        Analyse all configured log repositories for errors and exceptions from the past
        {_config.TimeRangeHours} hours (maximum {_config.MaxResults} results per repository).

        Steps:
        1. Call list_log_repositories to discover what repositories are available.
        2. For each repository, call generate_analysis_report to get a detailed Markdown report.
        3. Synthesise all per-repository reports into one final consolidated Markdown report.

        Today is {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC.
        """;
}
