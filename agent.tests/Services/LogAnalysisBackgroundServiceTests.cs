using AiOps.Agent.Configuration;
using AiOps.Agent.Models;
using AiOps.Agent.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiOps.Agent.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LogAnalysisBackgroundService"/>.
/// The Anthropic + MCP infrastructure is replaced by a mock <see cref="IAgentOrchestrator"/>.
/// Files are written to an isolated temp directory that is cleaned up after each test.
/// </summary>
public sealed class LogAnalysisBackgroundServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "aiops-tests-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly Mock<IAgentOrchestrator>          _orchestrator = new();
    private readonly Mock<IHostApplicationLifetime>    _lifetime     = new();
    private readonly Mock<ILogger<LogAnalysisBackgroundService>> _logger = new();

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private AgentConfig OneShotConfig() => new()
    {
        IntervalMinutes = 0,
        OutputDirectory = _tempDir,
        Model           = "test-model",
    };

    /// <summary>
    /// Creates a service, starts it, and waits for ExecuteAsync to complete
    /// (relies on <c>BackgroundService.ExecuteTask</c> which is public in .NET 6+).
    /// </summary>
    private async Task RunToCompletionAsync(
        AgentConfig config,
        CancellationToken ct = default)
    {
        var svc = new LogAnalysisBackgroundService(
            config, _orchestrator.Object, _logger.Object, _lifetime.Object);

        await svc.StartAsync(ct);
        // ExecuteTask is public on BackgroundService since .NET 6
        await svc.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private AnalysisRun MakeSuccessRun(DateTimeOffset? startedAt = null) => new()
    {
        StartedAt   = startedAt ?? DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow.AddSeconds(1),
        Success     = true,
        Model       = "test-model",
        InputTokens  = 100,
        OutputTokens = 50,
    };

    private AnalysisRun MakeFailureRun() => new()
    {
        StartedAt    = DateTimeOffset.UtcNow,
        CompletedAt  = DateTimeOffset.UtcNow.AddSeconds(1),
        Success      = false,
        Model        = "test-model",
        ErrorType    = "InvalidOperationException",
        ErrorMessage = "MCP server failed to start",
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Output directory
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CreatesOutputDirectory_IfMissing()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSuccessRun());

        Directory.Exists(_tempDir).Should().BeFalse("precondition: temp dir does not yet exist");

        await RunToCompletionAsync(OneShotConfig());

        Directory.Exists(_tempDir).Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // One-shot mode
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OneShotMode_InvokesOrchestratorExactlyOnce()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSuccessRun());

        await RunToCompletionAsync(OneShotConfig());

        _orchestrator.Verify(o => o.RunAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OneShotMode_StopsApplication()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSuccessRun());

        await RunToCompletionAsync(OneShotConfig());

        _lifetime.Verify(l => l.StopApplication(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OneShotMode_WritesExactlyOneJsonFile()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSuccessRun());

        await RunToCompletionAsync(OneShotConfig());

        var files = Directory.GetFiles(_tempDir, "*.json");
        files.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_OneShotMode_FileNameMatchesPattern()
    {
        var startedAt = new DateTimeOffset(2026, 5, 26, 12, 30, 45, TimeSpan.Zero);
        var run       = MakeSuccessRun(startedAt);
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        await RunToCompletionAsync(OneShotConfig());

        var files         = Directory.GetFiles(_tempDir, "*.json");
        var filename      = Path.GetFileName(files[0]);
        var expectedName  = $"analysis_20260526_123045_{run.RunId[..8]}.json";

        filename.Should().Be(expectedName);
    }

    [Fact]
    public async Task ExecuteAsync_OneShotMode_WrittenFileContainsValidJson()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSuccessRun());

        await RunToCompletionAsync(OneShotConfig());

        var path    = Directory.GetFiles(_tempDir, "*.json")[0];
        var content = await File.ReadAllTextAsync(path);

        content.Should().NotBeNullOrWhiteSpace();
        var act = () => System.Text.Json.JsonDocument.Parse(content);
        act.Should().NotThrow(because: "written file must be valid JSON");
    }

    [Fact]
    public async Task ExecuteAsync_OneShotMode_WrittenFileContainsRunId()
    {
        var run = MakeSuccessRun();
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        await RunToCompletionAsync(OneShotConfig());

        var content = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir, "*.json")[0]);
        content.Should().Contain(run.RunId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Failure handling
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_FailedRun_StillWritesJsonFile()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFailureRun());

        await RunToCompletionAsync(OneShotConfig());

        Directory.GetFiles(_tempDir, "*.json").Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_FailedRun_JsonFileContainsSuccessFalse()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFailureRun());

        await RunToCompletionAsync(OneShotConfig());

        var content = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir, "*.json")[0]);
        content.Should().Contain("\"success\": false");
    }

    [Fact]
    public async Task ExecuteAsync_FailedRun_JsonContainsErrorDetails()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFailureRun());

        await RunToCompletionAsync(OneShotConfig());

        var content = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir, "*.json")[0]);
        content.Should().Contain("InvalidOperationException")
            .And.Contain("MCP server failed to start");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Periodic mode
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PeriodicMode_RunsOrchestratorMultipleTimes()
    {
        var callCount = 0;
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(() => MakeSuccessRun());

        // Use a very short interval so we can cancel after 2+ runs
        var config = new AgentConfig
        {
            IntervalMinutes = 1.0 / 60.0 / 2.0, // ~0.5 seconds
            OutputDirectory = _tempDir,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var svc = new LogAnalysisBackgroundService(
            config, _orchestrator.Object, _logger.Object, _lifetime.Object);

        await svc.StartAsync(cts.Token);
        try { await svc.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { /* expected */ }

        callCount.Should().BeGreaterThanOrEqualTo(2,
            because: "periodic mode should run more than once within the timeout");
    }

    [Fact]
    public async Task ExecuteAsync_PeriodicMode_DoesNotCallStopApplication()
    {
        _orchestrator.Setup(o => o.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSuccessRun());

        // Very short interval, cancel quickly so we get one run without StopApplication
        var config = new AgentConfig
        {
            IntervalMinutes = 60, // long enough that the delay never elapses
            OutputDirectory = _tempDir,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var svc = new LogAnalysisBackgroundService(
            config, _orchestrator.Object, _logger.Object, _lifetime.Object);

        await svc.StartAsync(cts.Token);
        try { await svc.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) { /* expected */ }

        _lifetime.Verify(l => l.StopApplication(), Times.Never);
    }
}
