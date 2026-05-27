using AiOps.McpServer.Configuration;
using AiOps.McpServer.Models;
using AiOps.McpServer.Repositories;

namespace AiOps.McpServer.Tests;

/// <summary>
/// Tests the SQL query builder via the internal BuildQuery method.
/// These tests verify the generated SQL without needing a real database connection.
/// </summary>
public sealed class SqlLogRepositoryTests
{
    private static SqlLogRepository Repo(
        string dialect,
        string schema = "dbo",
        string table = "Logs",
        string? connectionString = "Server=.;Database=Test;") =>
        new($"test-{dialect}", new LogRepositoryConfig
        {
            Type = "Sql",
            ProviderName = "Microsoft.Data.SqlClient",
            SqlDialect = dialect,
            ConnectionString = connectionString,
            SchemaName = schema,
            TableName = table,
            TimestampColumn = "TimeStamp",
            LevelColumn = "Level",
            MessageColumn = "Message",
            MessageTemplateColumn = "MessageTemplate",
            ExceptionColumn = "Exception",
            PropertiesColumn = "Properties"
        });

    private static LogQueryOptions DefaultOptions(string? search = null, string[]? levels = null) => new()
    {
        From = DateTimeOffset.UtcNow.AddHours(-24),
        To = DateTimeOffset.UtcNow,
        Levels = levels ?? ["Error", "Fatal"],
        SearchTerm = search,
        MaxResults = 50
    };

    // ── Dialect quoting ───────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_SqlServerDialect_UsesBracketQuotes()
    {
        var sql = Repo("SqlServer").BuildQuery(DefaultOptions());

        sql.Should().Contain("[TimeStamp]")
            .And.Contain("[dbo].[Logs]");
    }

    [Fact]
    public void BuildQuery_AnsiDialect_UsesDoubleQuotes()
    {
        var sql = Repo("Ansi").BuildQuery(DefaultOptions());

        sql.Should().Contain("\"TimeStamp\"")
            .And.Contain("\"dbo\".\"Logs\"");
    }

    [Fact]
    public void BuildQuery_MySqlDialect_UsesBacktickQuotes()
    {
        var sql = Repo("MySql").BuildQuery(DefaultOptions());

        sql.Should().Contain("`TimeStamp`")
            .And.Contain("`dbo`.`Logs`");
    }

    // ── Row-limit clause ──────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_SqlServerDialect_UsesTopClause()
    {
        var sql = Repo("SqlServer").BuildQuery(DefaultOptions());

        sql.Should().Contain("TOP (@maxResults)")
            .And.NotContain("LIMIT");
    }

    [Fact]
    public void BuildQuery_AnsiDialect_UsesLimitClause()
    {
        var sql = Repo("Ansi").BuildQuery(DefaultOptions());

        sql.Should().Contain("LIMIT @maxResults")
            .And.NotContain("TOP");
    }

    [Fact]
    public void BuildQuery_MySqlDialect_UsesLimitClause()
    {
        var sql = Repo("MySql").BuildQuery(DefaultOptions());

        sql.Should().Contain("LIMIT @maxResults")
            .And.NotContain("TOP");
    }

    // ── Schema handling ───────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_EmptySchema_OmitsSchemaPrefix()
    {
        var sql = Repo("Ansi", schema: "").BuildQuery(DefaultOptions());

        sql.Should().Contain("\"Logs\"")
            .And.NotMatchRegex(@"""[^""]+""\.""Logs""");   // no schema.table pattern
    }

    [Fact]
    public void BuildQuery_NonDefaultSchema_IncludesSchemaPrefix()
    {
        var sql = Repo("Ansi", schema: "logging", table: "AppLogs").BuildQuery(DefaultOptions());

        sql.Should().Contain("\"logging\".\"AppLogs\"");
    }

    // ── WHERE clause predicates ───────────────────────────────────────────────

    [Fact]
    public void BuildQuery_WithLevels_IncludesInClause()
    {
        var sql = Repo("Ansi").BuildQuery(DefaultOptions());

        sql.Should().Contain("'Error'")
            .And.Contain("'Fatal'")
            .And.Contain(" IN (");
    }

    [Fact]
    public void BuildQuery_EmptyLevels_OmitsInClause()
    {
        var sql = Repo("Ansi").BuildQuery(DefaultOptions(levels: []));

        sql.Should().NotContain(" IN (");
    }

    [Fact]
    public void BuildQuery_WithSearchTerm_IncludesLikePredicates()
    {
        var sql = Repo("Ansi").BuildQuery(DefaultOptions(search: "timeout"));

        sql.Should().Contain("LIKE @searchTerm")
            .And.Contain("\"Message\"")
            .And.Contain("\"Exception\"");
    }

    [Fact]
    public void BuildQuery_NoSearchTerm_OmitsLikeClause()
    {
        var sql = Repo("Ansi").BuildQuery(DefaultOptions(search: null));

        sql.Should().NotContain("LIKE");
    }

    // ── Structural requirements ───────────────────────────────────────────────

    [Fact]
    public void BuildQuery_AlwaysIncludesOrderByDescending()
    {
        foreach (var dialect in new[] { "SqlServer", "Ansi", "MySql" })
        {
            Repo(dialect).BuildQuery(DefaultOptions())
                .Should().Contain("ORDER BY")
                .And.Contain("DESC");
        }
    }

    [Fact]
    public void BuildQuery_AlwaysSelectsAllRequiredColumns()
    {
        var sql = Repo("Ansi").BuildQuery(DefaultOptions());

        foreach (var col in new[] { "Timestamp", "Level", "Message", "MessageTemplate", "Exception", "Properties" })
            sql.Should().Contain(col, because: $"column alias {col} must be selected");
    }
}
