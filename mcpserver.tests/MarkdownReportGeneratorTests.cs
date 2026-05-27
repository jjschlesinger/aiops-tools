using AiOps.McpServer.Models;
using AiOps.McpServer.Services;

namespace AiOps.McpServer.Tests;

public sealed class MarkdownReportGeneratorTests
{
    private readonly MarkdownReportGenerator _generator = new();

    private static LogQueryOptions Options(int hoursBack = 24) => new()
    {
        From = DateTimeOffset.UtcNow.AddHours(-hoursBack),
        To = DateTimeOffset.UtcNow,
        Levels = ["Error", "Fatal"]
    };

    private static LogEntry Entry(
        string exceptionType = "System.Exception",
        string message = "Something failed",
        string? exceptionMessage = "test failure",
        DateTimeOffset? timestamp = null,
        string level = "Error") => new()
    {
        Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        Level = level,
        Message = message,
        ExceptionType = exceptionType,
        ExceptionMessage = exceptionMessage,
        Exception = $"{exceptionType}: {exceptionMessage}\n   at TestApp.Service.Run() in Service.cs:line 1"
    };

    // ── Header / metadata ─────────────────────────────────────────────────────

    [Fact]
    public void Generate_AlwaysContainsRepositoryName()
    {
        var report = _generator.Generate("prod-sql", Options(), []);

        report.Should().Contain("prod-sql");
    }

    [Fact]
    public void Generate_AlwaysContainsTotalCount()
    {
        var report = _generator.Generate("repo", Options(), [Entry(), Entry()]);

        report.Should().Contain("2");
    }

    // ── Empty results ─────────────────────────────────────────────────────────

    [Fact]
    public void Generate_NoEntries_ContainsNoErrorsMessage()
    {
        var report = _generator.Generate("repo", Options(), []);

        report.Should().Contain("No errors found");
    }

    [Fact]
    public void Generate_NoEntries_StillContainsHeader()
    {
        var report = _generator.Generate("repo", Options(), []);

        report.Should().StartWith("# Log Error Analysis Report");
    }

    // ── Executive summary ─────────────────────────────────────────────────────

    [Fact]
    public void Generate_WithEntries_ContainsExecutiveSummarySection()
    {
        var report = _generator.Generate("repo", Options(), [Entry()]);

        report.Should().Contain("## Executive Summary");
    }

    [Fact]
    public void Generate_MultipleExceptionTypes_ListsEachInSummary()
    {
        var entries = new[]
        {
            Entry("System.NullReferenceException"),
            Entry("System.TimeoutException"),
            Entry("System.NullReferenceException"),  // duplicate — should group
        };

        var report = _generator.Generate("repo", Options(), entries);

        report.Should().Contain("System.NullReferenceException")
            .And.Contain("System.TimeoutException");
    }

    [Fact]
    public void Generate_GroupedExceptions_ShowsCorrectCounts()
    {
        var entries = Enumerable.Repeat(Entry("System.NullReferenceException"), 5)
            .Concat(Enumerable.Repeat(Entry("System.TimeoutException"), 2))
            .ToList();

        var report = _generator.Generate("repo", Options(), entries);

        // The most frequent type should appear first
        var nullRefIdx    = report.IndexOf("System.NullReferenceException", StringComparison.Ordinal);
        var timeoutIdx    = report.IndexOf("System.TimeoutException",        StringComparison.Ordinal);

        nullRefIdx.Should().BeLessThan(timeoutIdx,
            because: "higher-frequency exception type should appear first");
    }

    // ── Exception detail sections ─────────────────────────────────────────────

    [Fact]
    public void Generate_WithException_IncludesStackTraceInCodeBlock()
    {
        var report = _generator.Generate("repo", Options(),
            [Entry("System.NullReferenceException", exceptionMessage: "Object ref not set")]);

        report.Should().Contain("```")
            .And.Contain("at TestApp.Service.Run()");
    }

    [Fact]
    public void Generate_ContainsExceptionDetailsSection()
    {
        var report = _generator.Generate("repo", Options(), [Entry()]);

        report.Should().Contain("## Exception Details");
    }

    // ── Timeline ──────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_WithEntries_ContainsTimelineSection()
    {
        var report = _generator.Generate("repo", Options(), [Entry()]);

        report.Should().Contain("## Error Timeline");
    }

    // ── Investigation & template ──────────────────────────────────────────────

    [Fact]
    public void Generate_WithEntries_ContainsInvestigationAreasSection()
    {
        var report = _generator.Generate("repo", Options(), [Entry()]);

        report.Should().Contain("## Recommended Investigation Areas");
    }

    [Fact]
    public void Generate_AlwaysContainsAnalysisTemplate()
    {
        var report = _generator.Generate("repo", Options(), []);

        report.Should().Contain("## Analysis & Recommended Fixes");
    }

    [Fact]
    public void Generate_NullReferenceException_ProducesNullGuardHint()
    {
        var report = _generator.Generate("repo", Options(),
            [Entry("System.NullReferenceException")]);

        report.Should().Contain("null guard", because: "NullReferenceException should produce a null-guard investigation hint");
    }

    [Fact]
    public void Generate_TimeoutException_ProducesLatencyHint()
    {
        var report = _generator.Generate("repo", Options(),
            [Entry("System.TimeoutException")]);

        report.Should().Contain("latency",
            because: "TimeoutException hint mentions downstream service latency");
    }
}
