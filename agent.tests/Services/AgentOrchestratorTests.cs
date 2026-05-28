using AiOps.Agent.Configuration;
using AiOps.Agent.Services;
using Microsoft.Extensions.Logging;

namespace AiOps.Agent.Tests.Services;

/// <summary>
/// Tests for <see cref="AgentOrchestrator"/> that verify observable behaviour
/// without touching real Anthropic / MCP infrastructure.
///
/// All tests use an invalid <c>McpServerCommand</c> so that the subprocess
/// launch fails immediately, exercising the exception-catching wrapper in
/// <c>RunAsync</c> without network calls or live processes.
/// </summary>
public sealed class AgentOrchestratorTests
{
    // A command that is guaranteed not to exist on any OS.
    private const string InvalidCommand = "__aiops_nonexistent_cmd__";

    private static AgentOrchestrator MakeOrchestrator(string? command = null) =>
        new AgentOrchestrator(
            new AgentConfig { McpServerCommand = command ?? InvalidCommand },
            Mock.Of<ILogger<AgentOrchestrator>>());

    // ─────────────────────────────────────────────────────────────────────────
    // RunAsync never throws
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WhenMcpServerCommandInvalid_NeverThrows()
    {
        var orchestrator = MakeOrchestrator();

        var act = async () => await orchestrator.RunAsync();

        await act.Should().NotThrowAsync(
            because: "RunAsync must capture all exceptions into the returned run");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Run metadata is always populated
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AlwaysReturnsNonNullRun()
    {
        var run = await MakeOrchestrator().RunAsync();
        run.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_RunId_IsNonEmptyGuid()
    {
        var run = await MakeOrchestrator().RunAsync();

        run.RunId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(run.RunId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_Model_MatchesConfiguredModel()
    {
        var config = new AgentConfig
        {
            McpServerCommand = InvalidCommand,
            Model            = "claude-expected-model",
        };
        var orchestrator = new AgentOrchestrator(config, Mock.Of<ILogger<AgentOrchestrator>>());

        var run = await orchestrator.RunAsync();

        run.Model.Should().Be("claude-expected-model");
    }

    [Fact]
    public async Task RunAsync_StartedAt_IsRecentUtcTime()
    {
        var before = DateTimeOffset.UtcNow;
        var run    = await MakeOrchestrator().RunAsync();
        var after  = DateTimeOffset.UtcNow;

        run.StartedAt.Should().BeOnOrAfter(before)
            .And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task RunAsync_CompletedAt_IsAlwaysSet()
    {
        var run = await MakeOrchestrator().RunAsync();

        run.CompletedAt.Should().NotBeNull(
            because: "CompletedAt is set in the finally block and must always be populated");
    }

    [Fact]
    public async Task RunAsync_CompletedAt_IsAfterStartedAt()
    {
        var run = await MakeOrchestrator().RunAsync();

        run.CompletedAt.Should().BeOnOrAfter(run.StartedAt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Failure path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WhenMcpServerFails_SetsSuccessFalse()
    {
        var run = await MakeOrchestrator().RunAsync();

        run.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenMcpServerFails_SetsErrorType()
    {
        var run = await MakeOrchestrator().RunAsync();

        run.ErrorType.Should().NotBeNullOrWhiteSpace(
            because: "the exception type name must be captured on failure");
    }

    [Fact]
    public async Task RunAsync_WhenMcpServerFails_SetsErrorMessage()
    {
        var run = await MakeOrchestrator().RunAsync();

        run.ErrorMessage.Should().NotBeNullOrWhiteSpace(
            because: "the exception message must be captured on failure");
    }

    [Fact]
    public async Task RunAsync_WhenMcpServerFails_TokenCountsRemainZero()
    {
        var run = await MakeOrchestrator().RunAsync();

        // No Anthropic calls were made because the process launch failed first.
        run.InputTokens.Should().Be(0);
        run.OutputTokens.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_WhenMcpServerFails_ToolCallsListIsEmpty()
    {
        var run = await MakeOrchestrator().RunAsync();

        run.ToolCalls.Should().BeEmpty(
            because: "no MCP calls could succeed if the server never started");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cancellation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithCancelledToken_StillReturnsCompletedRun()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var run = await MakeOrchestrator().RunAsync(cts.Token);

        run.Should().NotBeNull();
        run.CompletedAt.Should().NotBeNull();
    }
}
