using System.Text.Json;
using AiOps.McpServer.Models;
using AiOps.McpServer.Repositories;
using AiOps.McpServer.Services;
using AiOps.McpServer.Tools;

namespace AiOps.McpServer.Tests.Tools;

public sealed class LogAnalysisToolTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private readonly Mock<ILogRepositoryFactory> _factoryMock = new();
    private readonly MarkdownReportGenerator _reportGenerator = new();

    private LogAnalysisTool Tool() => new(_factoryMock.Object, _reportGenerator);

    private Mock<ILogRepository> SetupRepo(
        string name,
        string type = "Sql",
        IReadOnlyList<LogEntry>? entries = null)
    {
        var repoMock = new Mock<ILogRepository>();
        repoMock.Setup(r => r.Name).Returns(name);
        repoMock.Setup(r => r.RepositoryType).Returns(type);
        repoMock
            .Setup(r => r.QueryErrorsAsync(It.IsAny<LogQueryOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries ?? []);

        _factoryMock.Setup(f => f.GetRepository(name)).Returns(repoMock.Object);
        return repoMock;
    }

    private static LogEntry MakeEntry(
        string exceptionType = "System.Exception",
        string message = "Test error") => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Level = "Error",
        Message = message,
        ExceptionType = exceptionType,
        ExceptionMessage = "test",
        Exception = $"{exceptionType}: test\n   at TestApp.Run()"
    };

    // ── list_log_repositories ─────────────────────────────────────────────────

    [Fact]
    public void ListLogRepositories_NoRepositoriesConfigured_ReturnsConfigurationMessage()
    {
        _factoryMock.Setup(f => f.GetAvailableRepositories())
            .Returns(new Dictionary<string, string>());

        var result = Tool().ListLogRepositories();

        result.Should().Contain("No log repositories are configured");
    }

    [Fact]
    public void ListLogRepositories_WithRepositories_ListsEachNameAndType()
    {
        _factoryMock.Setup(f => f.GetAvailableRepositories())
            .Returns(new Dictionary<string, string>
            {
                ["prod-sql"]   = "Sql",
                ["prod-azure"] = "AzureMonitor",
            });

        var result = Tool().ListLogRepositories();

        result.Should().Contain("prod-sql")
            .And.Contain("prod-azure")
            .And.Contain("Sql")
            .And.Contain("AzureMonitor");
    }

    [Fact]
    public void ListLogRepositories_WithRepositories_MentionsRepositoryNameParameter()
    {
        _factoryMock.Setup(f => f.GetAvailableRepositories())
            .Returns(new Dictionary<string, string> { ["x"] = "Sql" });

        var result = Tool().ListLogRepositories();

        result.Should().Contain("repositoryName");
    }

    // ── query_log_errors ──────────────────────────────────────────────────────

    [Fact]
    public async Task QueryLogErrors_ValidRepo_ReturnsValidJson()
    {
        SetupRepo("prod", entries: [MakeEntry()]);

        var json = await Tool().QueryLogErrors("prod");

        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow("result must be valid JSON");
    }

    [Fact]
    public async Task QueryLogErrors_ValidRepo_JsonContainsTotalFound()
    {
        SetupRepo("prod", entries: [MakeEntry(), MakeEntry()]);

        var json = await Tool().QueryLogErrors("prod");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalFound").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task QueryLogErrors_ValidRepo_JsonContainsRepositoryName()
    {
        SetupRepo("prod");

        var json = await Tool().QueryLogErrors("prod");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("repositoryName").GetString().Should().Be("prod");
    }

    [Fact]
    public async Task QueryLogErrors_NoResults_ReturnsTotalFoundOfZero()
    {
        SetupRepo("prod", entries: []);

        var json = await Tool().QueryLogErrors("prod");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalFound").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task QueryLogErrors_UnknownRepository_ReturnsErrorJson()
    {
        _factoryMock
            .Setup(f => f.GetRepository("missing"))
            .Throws(new InvalidOperationException("No repo named 'missing'"));

        var json = await Tool().QueryLogErrors("missing");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString()
            .Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task QueryLogErrors_PassesTimeRangeHoursToQuery()
    {
        LogQueryOptions? captured = null;
        var repoMock = new Mock<ILogRepository>();
        repoMock.Setup(r => r.Name).Returns("prod");
        repoMock.Setup(r => r.RepositoryType).Returns("Sql");
        repoMock
            .Setup(r => r.QueryErrorsAsync(It.IsAny<LogQueryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<LogQueryOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync([]);
        _factoryMock.Setup(f => f.GetRepository("prod")).Returns(repoMock.Object);

        await Tool().QueryLogErrors("prod", timeRangeHours: 48);

        captured.Should().NotBeNull();
        captured!.From.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(-48), precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task QueryLogErrors_MaxResultsClampsAt500()
    {
        LogQueryOptions? captured = null;
        var repoMock = new Mock<ILogRepository>();
        repoMock.Setup(r => r.Name).Returns("prod");
        repoMock.Setup(r => r.RepositoryType).Returns("Sql");
        repoMock
            .Setup(r => r.QueryErrorsAsync(It.IsAny<LogQueryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<LogQueryOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync([]);
        _factoryMock.Setup(f => f.GetRepository("prod")).Returns(repoMock.Object);

        await Tool().QueryLogErrors("prod", maxResults: 9999);

        captured!.MaxResults.Should().Be(500);
    }

    // ── generate_analysis_report ──────────────────────────────────────────────

    [Fact]
    public async Task GenerateAnalysisReport_ValidRepo_ReturnsMarkdownReport()
    {
        SetupRepo("prod", entries: [MakeEntry("System.NullReferenceException")]);

        var report = await Tool().GenerateAnalysisReport("prod");

        report.Should().StartWith("# Log Error Analysis Report");
    }

    [Fact]
    public async Task GenerateAnalysisReport_ValidRepo_ReportContainsExceptionType()
    {
        SetupRepo("prod", entries: [MakeEntry("System.TimeoutException")]);

        var report = await Tool().GenerateAnalysisReport("prod");

        report.Should().Contain("System.TimeoutException");
    }

    [Fact]
    public async Task GenerateAnalysisReport_UnknownRepository_ReturnsErrorMarkdown()
    {
        _factoryMock
            .Setup(f => f.GetRepository("bad-repo"))
            .Throws(new InvalidOperationException("Not found"));

        var report = await Tool().GenerateAnalysisReport("bad-repo");

        report.Should().Contain("# Report Generation Failed")
            .And.Contain("InvalidOperationException");
    }

    [Fact]
    public async Task GenerateAnalysisReport_EmptyResults_ReturnsNoErrorsReport()
    {
        SetupRepo("prod", entries: []);

        var report = await Tool().GenerateAnalysisReport("prod");

        report.Should().Contain("No errors found");
    }
}
