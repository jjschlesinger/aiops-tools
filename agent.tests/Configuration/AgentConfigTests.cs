using AiOps.Agent.Configuration;

namespace AiOps.Agent.Tests.Configuration;

public sealed class AgentConfigTests
{
    private readonly AgentConfig _sut = new();

    // ── McpServer defaults ────────────────────────────────────────────────────

    [Fact]
    public void Default_McpServerCommand_IsDotnet()
        => _sut.McpServerCommand.Should().Be("dotnet");

    [Fact]
    public void Default_McpServerArguments_IsEmpty()
    {
        // The real defaults come from appsettings.json, not the C# initialiser.
        // The initialiser is intentionally [] so that .NET's ConfigurationBinder
        // replaces (not appends to) the array when it binds config values.
        _sut.McpServerArguments.Should().BeEmpty(
            because: "array defaults live in appsettings.json to avoid ConfigurationBinder merge behaviour");
    }

    [Fact]
    public void Default_McpServerWorkingDirectory_IsNull()
        => _sut.McpServerWorkingDirectory.Should().BeNull();

    // ── Claude model ──────────────────────────────────────────────────────────

    [Fact]
    public void Default_Model_IsNonEmpty()
        => _sut.Model.Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void Default_Model_ContainsClaude()
        => _sut.Model.Should().Contain("claude", because: "model name must reference a Claude model");

    // ── Analysis parameters ───────────────────────────────────────────────────

    [Fact]
    public void Default_TimeRangeHours_Is24()
        => _sut.TimeRangeHours.Should().Be(24);

    [Fact]
    public void Default_MaxResults_Is200()
        => _sut.MaxResults.Should().Be(200);

    [Fact]
    public void Default_MaxTokensPerTurn_Is8192()
        => _sut.MaxTokensPerTurn.Should().Be(8192);

    // ── Scheduling ────────────────────────────────────────────────────────────

    [Fact]
    public void Default_IntervalMinutes_IsPositive()
        => _sut.IntervalMinutes.Should().BeGreaterThan(0.0,
            because: "default mode is periodic, not one-shot");

    [Fact]
    public void Default_OutputDirectory_IsResults()
        => _sut.OutputDirectory.Should().Be("results");

    // ── One-shot semantics ────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(-100.0)]
    public void OneShotMode_WhenIntervalMinutesIsZeroOrNegative_PropertiesAccepted(double interval)
    {
        var config = new AgentConfig { IntervalMinutes = interval };
        config.IntervalMinutes.Should().Be(interval);
    }

    // ── Mutability ────────────────────────────────────────────────────────────

    [Fact]
    public void AllProperties_CanBeOverridden()
    {
        var config = new AgentConfig
        {
            McpServerCommand    = "my-server",
            McpServerArguments  = ["--port", "9000"],
            Model               = "claude-3-5-sonnet-20241022",
            TimeRangeHours      = 48,
            MaxResults          = 500,
            IntervalMinutes     = 30,
            OutputDirectory     = "/var/aiops/results",
            MaxTokensPerTurn    = 4096,
        };

        config.McpServerCommand.Should().Be("my-server");
        config.McpServerArguments.Should().Equal("--port", "9000");
        config.Model.Should().Be("claude-3-5-sonnet-20241022");
        config.TimeRangeHours.Should().Be(48);
        config.MaxResults.Should().Be(500);
        config.IntervalMinutes.Should().Be(30);
        config.OutputDirectory.Should().Be("/var/aiops/results");
        config.MaxTokensPerTurn.Should().Be(4096);
    }
}
