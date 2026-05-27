using AiOps.McpServer.Repositories;

namespace AiOps.McpServer.Tests;

public sealed class LogRepositoryFactoryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<ILogRepository> Repo(string name, string type = "Sql")
    {
        var m = new Mock<ILogRepository>();
        m.Setup(r => r.Name).Returns(name);
        m.Setup(r => r.RepositoryType).Returns(type);
        return m;
    }

    // ── GetRepository ─────────────────────────────────────────────────────────

    [Fact]
    public void GetRepository_KnownName_ReturnsMatchingInstance()
    {
        var repo = Repo("prod-sql");
        var factory = new LogRepositoryFactory([repo.Object]);

        factory.GetRepository("prod-sql").Should().BeSameAs(repo.Object);
    }

    [Fact]
    public void GetRepository_NameMatchIsCaseInsensitive()
    {
        var repo = Repo("Prod-SQL");
        var factory = new LogRepositoryFactory([repo.Object]);

        factory.GetRepository("prod-sql").Should().BeSameAs(repo.Object);
    }

    [Fact]
    public void GetRepository_UnknownName_ThrowsInvalidOperationException()
    {
        var factory = new LogRepositoryFactory([Repo("prod-sql").Object]);

        var act = () => factory.GetRepository("does-not-exist");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does-not-exist*")
            .WithMessage("*prod-sql*");   // error message lists available names
    }

    [Fact]
    public void GetRepository_EmptyCollection_ThrowsWithHelpfulMessage()
    {
        var factory = new LogRepositoryFactory([]);

        var act = () => factory.GetRepository("anything");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*anything*");
    }

    [Fact]
    public void GetRepository_MultipleRepos_ReturnsCorrectOne()
    {
        var sql     = Repo("prod-sql",    "Sql");
        var serilog = Repo("prod-logs",   "SerilogFile");
        var azure   = Repo("prod-azure",  "AzureMonitor");
        var factory = new LogRepositoryFactory([sql.Object, serilog.Object, azure.Object]);

        factory.GetRepository("prod-logs").Should().BeSameAs(serilog.Object);
    }

    // ── GetAvailableRepositories ──────────────────────────────────────────────

    [Fact]
    public void GetAvailableRepositories_ReturnsAllNamesAndTypes()
    {
        var repos = new[]
        {
            Repo("prod-serilog", "SerilogFile").Object,
            Repo("prod-sql",     "Sql").Object,
            Repo("prod-azure",   "AzureMonitor").Object,
        };
        var factory = new LogRepositoryFactory(repos);

        var result = factory.GetAvailableRepositories();

        result.Should().HaveCount(3);
        result["prod-serilog"].Should().Be("SerilogFile");
        result["prod-sql"].Should().Be("Sql");
        result["prod-azure"].Should().Be("AzureMonitor");
    }

    [Fact]
    public void GetAvailableRepositories_EmptyCollection_ReturnsEmptyDictionary()
    {
        var factory = new LogRepositoryFactory([]);

        factory.GetAvailableRepositories().Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableRepositories_KeyLookupIsCaseInsensitive()
    {
        var factory = new LogRepositoryFactory([Repo("Prod-SQL", "Sql").Object]);

        var result = factory.GetAvailableRepositories();

        result["prod-sql"].Should().Be("Sql");
    }
}
