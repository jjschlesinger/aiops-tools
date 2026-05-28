using System.Text.Json;
using AiOps.Agent.Models;

namespace AiOps.Agent.Tests.Models;

public sealed class AnalysisRunTests
{
    // ── Construction ──────────────────────────────────────────────────────────

    [Fact]
    public void RunId_OnConstruction_IsValidGuid()
    {
        var run = new AnalysisRun();
        Guid.TryParse(run.RunId, out _).Should().BeTrue(
            because: "RunId must be a parseable GUID");
    }

    [Fact]
    public void RunId_TwoInstances_AreUnique()
    {
        var a = new AnalysisRun();
        var b = new AnalysisRun();
        a.RunId.Should().NotBe(b.RunId);
    }

    [Fact]
    public void ToolCalls_OnConstruction_IsEmpty()
        => new AnalysisRun().ToolCalls.Should().BeEmpty();

    [Fact]
    public void Success_DefaultsToFalse()
        => new AnalysisRun().Success.Should().BeFalse();

    [Fact]
    public void TokenCounts_DefaultToZero()
    {
        var run = new AnalysisRun();
        run.InputTokens.Should().Be(0);
        run.OutputTokens.Should().Be(0);
    }

    // ── JSON property names ───────────────────────────────────────────────────

    [Fact]
    public void Serialization_RunId_UsesJsonPropertyName()
    {
        var json = JsonSerializer.Serialize(new AnalysisRun());
        json.Should().Contain("\"runId\"");
    }

    [Fact]
    public void Serialization_InputOutputTokens_UseCamelCaseNames()
    {
        var run = new AnalysisRun { InputTokens = 100, OutputTokens = 50 };
        var json = JsonSerializer.Serialize(run);
        json.Should().Contain("\"inputTokens\"")
            .And.Contain("\"outputTokens\"");
    }

    [Fact]
    public void Serialization_ToolCalls_UsesCorrectKey()
    {
        var json = JsonSerializer.Serialize(new AnalysisRun());
        json.Should().Contain("\"toolCalls\"");
    }

    [Fact]
    public void Serialization_Success_UsesCorrectKey()
    {
        var json = JsonSerializer.Serialize(new AnalysisRun { Success = true });
        json.Should().Contain("\"success\"");
    }

    // ── Round-trip serialization ──────────────────────────────────────────────

    [Fact]
    public void Serialization_RoundTrip_PreservesAllFields()
    {
        var original = new AnalysisRun
        {
            StartedAt   = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 5, 26, 10, 5, 0, TimeSpan.Zero),
            Success     = true,
            Model       = "claude-opus-4-5-20250929",
            InputTokens  = 1234,
            OutputTokens = 567,
            FinalReport  = "# Report\nAll good.",
        };

        var json     = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AnalysisRun>(json)!;

        restored.RunId.Should().Be(original.RunId);
        restored.StartedAt.Should().Be(original.StartedAt);
        restored.CompletedAt.Should().Be(original.CompletedAt);
        restored.Success.Should().BeTrue();
        restored.Model.Should().Be(original.Model);
        restored.InputTokens.Should().Be(1234);
        restored.OutputTokens.Should().Be(567);
        restored.FinalReport.Should().Be(original.FinalReport);
    }

    [Fact]
    public void Serialization_NullableFields_AreNullWhenNotSet()
    {
        var json     = JsonSerializer.Serialize(new AnalysisRun());
        var restored = JsonSerializer.Deserialize<AnalysisRun>(json)!;

        restored.CompletedAt.Should().BeNull();
        restored.FinalReport.Should().BeNull();
        restored.ErrorType.Should().BeNull();
        restored.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Serialization_ToolCallRecords_RoundTripCorrectly()
    {
        var run = new AnalysisRun();
        run.ToolCalls.Add(new ToolCallRecord
        {
            ToolName   = "list_log_repositories",
            Input      = "{\"repo\":\"prod\"}",
            Output     = "repo-a\nrepo-b",
            CalledAt   = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
            DurationMs = 42,
            IsError    = false,
        });

        var json     = JsonSerializer.Serialize(run);
        var restored = JsonSerializer.Deserialize<AnalysisRun>(json)!;

        restored.ToolCalls.Should().HaveCount(1);
        var record = restored.ToolCalls[0];
        record.ToolName.Should().Be("list_log_repositories");
        record.Input.Should().Be("{\"repo\":\"prod\"}");
        record.Output.Should().Be("repo-a\nrepo-b");
        record.DurationMs.Should().Be(42);
        record.IsError.Should().BeFalse();
    }
}

public sealed class ToolCallRecordTests
{
    [Fact]
    public void Serialization_UsesCorrectJsonPropertyNames()
    {
        var record = new ToolCallRecord
        {
            ToolName   = "my_tool",
            Input      = "{}",
            Output     = "result",
            DurationMs = 100,
            IsError    = true,
        };

        var json = JsonSerializer.Serialize(record);

        json.Should().Contain("\"toolName\"")
            .And.Contain("\"input\"")
            .And.Contain("\"output\"")
            .And.Contain("\"durationMs\"")
            .And.Contain("\"isError\"")
            .And.Contain("\"calledAt\"");
    }

    [Fact]
    public void Default_Input_IsEmptyJsonObject()
        => new ToolCallRecord().Input.Should().Be("{}");

    [Fact]
    public void Default_IsError_IsFalse()
        => new ToolCallRecord().IsError.Should().BeFalse();
}
