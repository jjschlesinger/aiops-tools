using AiOps.McpServer.Configuration;
using AiOps.McpServer.Models;
using AiOps.McpServer.Repositories;

namespace AiOps.McpServer.Tests;

public sealed class SerilogFileLogRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public SerilogFileLogRepositoryTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── ParseExceptionHeader ──────────────────────────────────────────────────

    [Fact]
    public void ParseExceptionHeader_Null_ReturnsBothNull()
    {
        var (type, msg) = SerilogFileLogRepository.ParseExceptionHeader(null);

        type.Should().BeNull();
        msg.Should().BeNull();
    }

    [Fact]
    public void ParseExceptionHeader_EmptyString_ReturnsBothNull()
    {
        var (type, msg) = SerilogFileLogRepository.ParseExceptionHeader("   ");

        type.Should().BeNull();
        msg.Should().BeNull();
    }

    [Fact]
    public void ParseExceptionHeader_StandardException_ExtractsTypeAndMessage()
    {
        const string ex = "System.NullReferenceException: Object reference not set to an instance of an object.";

        var (type, msg) = SerilogFileLogRepository.ParseExceptionHeader(ex);

        type.Should().Be("System.NullReferenceException");
        msg.Should().Be("Object reference not set to an instance of an object.");
    }

    [Fact]
    public void ParseExceptionHeader_MultilineException_OnlyParsesFirstLine()
    {
        const string ex = """
            System.InvalidOperationException: Something went wrong.
               at MyApp.Service.DoWork() in /src/Service.cs:line 42
               at MyApp.Controllers.HomeController.Index()
            """;

        var (type, msg) = SerilogFileLogRepository.ParseExceptionHeader(ex);

        type.Should().Be("System.InvalidOperationException");
        msg.Should().Be("Something went wrong.");
    }

    [Fact]
    public void ParseExceptionHeader_TypeWithNoMessage_ReturnsTypeAndEmptyMessage()
    {
        const string ex = "System.Exception:";

        var (type, msg) = SerilogFileLogRepository.ParseExceptionHeader(ex);

        type.Should().Be("System.Exception");
        msg.Should().BeEmpty();
    }

    [Fact]
    public void ParseExceptionHeader_NoColon_ReturnsFullLineAsTypeAndNullMessage()
    {
        // When there is no colon at all the implementation treats the whole
        // line as the raw type string and returns null for the message.
        const string ex = "Something went very wrong without an exception type";

        var (type, msg) = SerilogFileLogRepository.ParseExceptionHeader(ex);

        type.Should().Be(ex);
        msg.Should().BeNull();
    }

    // ── File querying ─────────────────────────────────────────────────────────

    private SerilogFileLogRepository BuildRepo(string? pattern = "*.clef") =>
        new("test", new LogRepositoryConfig
        {
            Type = "SerilogFile",
            Directory = _tempDir,
            FilePattern = pattern ?? "*.clef"
        });

    private void WriteClef(string fileName, params string[] jsonLines) =>
        File.WriteAllLines(Path.Combine(_tempDir, fileName), jsonLines);

    private static string ClefLine(
        string level,
        string message,
        DateTimeOffset? timestamp = null,
        string? exception = null)
    {
        var ts = (timestamp ?? DateTimeOffset.UtcNow).ToString("O");
        var ex = exception is null ? "" : $@",""@x"":""{exception.Replace("\"", "\\\"")}""";
        return $@"{{""@t"":""{ts}"",""@mt"":""{message}"",""@l"":""{level}""{ex}}}";
    }

    [Fact]
    public async Task QueryErrorsAsync_MatchingLevelAndTimeRange_ReturnsEntries()
    {
        var now = DateTimeOffset.UtcNow;
        WriteClef("app.clef",
            ClefLine("Error", "Something failed", now),
            ClefLine("Information", "Started up", now));

        var options = new LogQueryOptions
        {
            From = now.AddMinutes(-1),
            To = now.AddMinutes(1),
            Levels = ["Error"]
        };

        var results = await BuildRepo().QueryErrorsAsync(options);

        results.Should().HaveCount(1);
        results[0].Message.Should().Be("Something failed");
        results[0].Level.Should().Be("Error");
    }

    [Fact]
    public async Task QueryErrorsAsync_EntryOutsideTimeRange_IsExcluded()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-2);
        WriteClef("app.clef", ClefLine("Error", "Old error", old));

        var options = new LogQueryOptions
        {
            From = DateTimeOffset.UtcNow.AddHours(-1),
            To = DateTimeOffset.UtcNow
        };

        var results = await BuildRepo().QueryErrorsAsync(options);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryErrorsAsync_SearchTermFiltersOnMessage()
    {
        var now = DateTimeOffset.UtcNow;
        WriteClef("app.clef",
            ClefLine("Error", "Database connection timeout", now),
            ClefLine("Error", "Unhandled exception in handler", now));

        var options = new LogQueryOptions
        {
            From = now.AddMinutes(-1),
            To = now.AddMinutes(1),
            Levels = ["Error"],
            SearchTerm = "Database"
        };

        var results = await BuildRepo().QueryErrorsAsync(options);

        results.Should().HaveCount(1);
        results[0].Message.Should().Contain("Database");
    }

    [Fact]
    public async Task QueryErrorsAsync_MaxResultsLimitsOutput()
    {
        var now = DateTimeOffset.UtcNow;
        var lines = Enumerable.Range(1, 20)
            .Select(i => ClefLine("Error", $"Error {i}", now.AddSeconds(i)))
            .ToArray();
        WriteClef("app.clef", lines);

        var options = new LogQueryOptions
        {
            From = now.AddMinutes(-1),
            To = now.AddMinutes(5),
            MaxResults = 5
        };

        var results = await BuildRepo().QueryErrorsAsync(options);

        results.Should().HaveCount(5);
    }

    [Fact]
    public async Task QueryErrorsAsync_ExceptionField_IsPopulatedAndParsed()
    {
        var now = DateTimeOffset.UtcNow;
        WriteClef("app.clef",
            ClefLine("Error", "Crash", now,
                exception: "System.NullReferenceException: Object reference not set.\\n   at Foo.Bar()"));

        var options = new LogQueryOptions
        {
            From = now.AddMinutes(-1),
            To = now.AddMinutes(1)
        };

        var results = await BuildRepo().QueryErrorsAsync(options);

        results.Should().HaveCount(1);
        results[0].ExceptionType.Should().Be("System.NullReferenceException");
        results[0].ExceptionMessage.Should().Be("Object reference not set.");
    }

    [Fact]
    public async Task QueryErrorsAsync_MissingDirectory_ReturnsEmpty()
    {
        var repo = new SerilogFileLogRepository("test", new LogRepositoryConfig
        {
            Type = "SerilogFile",
            Directory = Path.Combine(_tempDir, "nonexistent"),
            FilePattern = "*.clef"
        });

        var options = new LogQueryOptions { From = DateTimeOffset.UtcNow.AddHours(-1) };

        var results = await repo.QueryErrorsAsync(options);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryErrorsAsync_ResultsOrderedByDescendingTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        WriteClef("app.clef",
            ClefLine("Error", "First",  now.AddMinutes(-5)),
            ClefLine("Error", "Second", now.AddMinutes(-3)),
            ClefLine("Error", "Third",  now.AddMinutes(-1)));

        var options = new LogQueryOptions
        {
            From = now.AddMinutes(-10),
            To = now.AddMinutes(1)
        };

        var results = await BuildRepo().QueryErrorsAsync(options);

        results.Select(r => r.Message)
            .Should().ContainInOrder("Third", "Second", "First");
    }
}
