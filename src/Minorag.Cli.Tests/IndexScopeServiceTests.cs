using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Minorag.Cli.Indexing;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Store;

namespace Minorag.Cli.Tests;

public class IndexScopeServiceTests
{
    private static RagDbContext CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseSqlite(conn)
            .Options;

        var ctx = new RagDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static DirectoryInfo FakeRepo(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "minorag-tests", name);
        Directory.CreateDirectory(dir);
        return new DirectoryInfo(dir);
    }

    [Fact]
    public async Task NoClientNoProject_CreatesOnlyRepository()
    {
        using var ctx = CreateContext();
        var service = new IndexScopeService(ctx);
        var repoRoot = FakeRepo("no_project");

        var repo = await service.EnsureClientProjectRepoAsync(
            repoRoot,
            clientName: null,
            projectName: null,
            ct: CancellationToken.None);

        Assert.NotNull(repo);
        Assert.Null(repo.ProjectId);
        Assert.Empty(await ctx.Clients.ToListAsync());
        Assert.Empty(await ctx.Projects.ToListAsync());
    }

    [Fact]
    public async Task ClientOnly_CreatesClientAndRepo()
    {
        using var ctx = CreateContext();
        var service = new IndexScopeService(ctx);
        var repoRoot = FakeRepo("client-only");

        var repo = await service.EnsureClientProjectRepoAsync(
            repoRoot,
            clientName: "Acme Co",
            projectName: null,
            ct: CancellationToken.None);

        Assert.NotNull(repo);
        Assert.Null(repo.ProjectId);

        var clients = await ctx.Clients.ToListAsync();
        Assert.Single(clients);
        Assert.Equal("Acme Co", clients[0].Name);
    }

    [Fact]
    public async Task ProjectOnly_CreatesLocalClientAndProject()
    {
        using var ctx = CreateContext();
        var service = new IndexScopeService(ctx);
        var repoRoot = FakeRepo("project-only");

        var repo = await service.EnsureClientProjectRepoAsync(
            repoRoot,
            clientName: null,
            projectName: "Minorag",
            ct: CancellationToken.None);

        var project = await ctx.Projects.Include(p => p.Client).SingleAsync();
        Assert.Equal("Minorag", project.Name);
        Assert.Equal("Local", project.Client.Name);
    }

    [Fact]
    public async Task Idempotent_RepoAlreadyLinkedToSameProject()
    {
        using var ctx = CreateContext();
        var service = new IndexScopeService(ctx);
        var repoRoot = FakeRepo("idempotent");

        var repo1 = await service.EnsureClientProjectRepoAsync(
            repoRoot, "ClientA", "ProjA", CancellationToken.None);

        var repo2 = await service.EnsureClientProjectRepoAsync(
            repoRoot, "ClientA", "ProjA", CancellationToken.None);

        Assert.Equal(repo1.Id, repo2.Id);
    }

    [Fact]
    public async Task Reassignment_WhenUserDeclines_Throws()
    {
        using var ctx = CreateContext();

        var oldClient = new Client { Name = "OldClient", Slug = "old_client" };
        ctx.Clients.Add(oldClient);
        await ctx.SaveChangesAsync();

        var oldProj = new Project { Name = "OldProject", Slug = "old_project", ClientId = oldClient.Id };
        ctx.Projects.Add(oldProj);
        await ctx.SaveChangesAsync();

        var repoRoot = FakeRepo("decline-reassign");
        var repo = new Repository
        {
            Name = repoRoot.Name,
            RootPath = repoRoot.FullName,
            ProjectId = oldProj.Id
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();

        var service = new IndexScopeService(ctx);

        var backup = Console.In;
        Console.SetIn(new StringReader("n\n"));

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await service.EnsureClientProjectRepoAsync(
                    repoRoot, "NewClient", "NewProject", CancellationToken.None);
            });

            var stillOld = await ctx.Repositories.SingleAsync(r => r.Id == repo.Id);
            Assert.Equal(oldProj.Id, stillOld.ProjectId);
        }
        finally
        {
            Console.SetIn(backup);
        }
    }

    [Fact]
    public async Task Reassignment_WhenUserAccepts_UpdatesProject()
    {
        using var ctx = CreateContext();

        var oldClient = new Client { Name = "OldClient", Slug = "old_client" };
        ctx.Clients.Add(oldClient);
        await ctx.SaveChangesAsync();

        var oldProj = new Project { Name = "OldProject", Slug = "old_project", ClientId = oldClient.Id };
        ctx.Projects.Add(oldProj);
        await ctx.SaveChangesAsync();

        var repoRoot = FakeRepo("accept-reassign");
        var repo = new Repository
        {
            Name = repoRoot.Name,
            RootPath = repoRoot.FullName,
            ProjectId = oldProj.Id
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();

        var service = new IndexScopeService(ctx);

        var backup = Console.In;
        Console.SetIn(new StringReader("y\n"));

        try
        {
            var updated = await service.EnsureClientProjectRepoAsync(
                repoRoot, "NewClient", "NewProject", CancellationToken.None);

            Assert.NotNull(updated.ProjectId);

            var newProj = await ctx.Projects.Include(p => p.Client)
                .SingleAsync(p => p.Id == updated.ProjectId);

            Assert.Equal("NewProject", newProj.Name);
            Assert.Equal("NewClient", newProj.Client.Name);
        }
        finally
        {
            Console.SetIn(backup);
        }
    }
}