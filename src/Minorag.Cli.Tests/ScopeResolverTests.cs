using Minorag.Cli.Models.Domain;
using Minorag.Cli.Services;
using Minorag.Cli.Tests.TestInfrastructure;

namespace Minorag.Cli.Tests;

public class ScopeResolverTests
{
    [Fact]
    public async Task NoRepositories_ThrowsHelpfulError()
    {
        await using var ctx = SqliteTestContextFactory.CreateContext();
        var resolver = new ScopeResolver(ctx);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveScopeAsync(
                currentDirectory: "/tmp",
                repoNames: [],
                reposCsv: null,
                projectName: null,
                clientName: null,
                allReposFlag: false,
                ct: CancellationToken.None));

        Assert.Contains("No repositories found in the index", ex.Message);
    }

    [Fact]
    public async Task AllReposFlag_ReturnsAllRepositories()
    {
        await using var ctx = SqliteTestContextFactory.CreateContext();

        var client = new Client { Name = "Acme", Slug = "acme" };
        var project = new Project { Name = "Backend", Slug = "backend", Client = client };

        var repo1 = new Repository { Name = "api-service", RootPath = "/tmp/acme/api", Project = project };
        var repo2 = new Repository { Name = "worker-service", RootPath = "/tmp/acme/worker", Project = project };

        ctx.Clients.Add(client);
        ctx.Projects.Add(project);
        ctx.Repositories.AddRange(repo1, repo2);
        await ctx.SaveChangesAsync();

        var resolver = new ScopeResolver(ctx);

        var scoped = await resolver.ResolveScopeAsync(
            currentDirectory: "/some/other/path",
            repoNames: [],
            reposCsv: null,
            projectName: null,
            clientName: null,
            allReposFlag: true,
            ct: CancellationToken.None);

        Assert.Equal(2, scoped.Count);
        Assert.Contains(scoped, r => r.Name == "api-service");
        Assert.Contains(scoped, r => r.Name == "worker-service");
    }

    private static readonly string[] expected = ["api", "ui"];
    private static readonly string[] expectedArray = ["api", "worker"];
    private static readonly string[] expectedArray0 = ["api", "worker"];
    private static readonly string[] expectedArray1 = new[] { "api", "worker" };

    [Fact]
    public async Task ExplicitRepoNames_UsesUnionOfRepoAndReposCsv()
    {
        await using var ctx = SqliteTestContextFactory.CreateContext();

        var repo1 = new Repository { Name = "api", RootPath = "/tmp/api" };
        var repo2 = new Repository { Name = "ui", RootPath = "/tmp/ui" };
        var repo3 = new Repository { Name = "worker", RootPath = "/tmp/worker" };

        ctx.Repositories.AddRange(repo1, repo2, repo3);
        await ctx.SaveChangesAsync();

        var resolver = new ScopeResolver(ctx);

        var scoped = await resolver.ResolveScopeAsync(
            currentDirectory: "/tmp",
            repoNames: new[] { "api" }, // --repo api
            reposCsv: "ui,missing-repo", // --repos ui,missing-repo
            projectName: null,
            clientName: null,
            allReposFlag: false,
            ct: CancellationToken.None);

        var names = scoped.Select(r => r.Name).OrderBy(n => n).ToArray();
        Assert.Equal(expected, names);
    }

    [Fact]
    public async Task ProjectScope_ReturnsAllReposInProject()
    {
        await using var ctx = SqliteTestContextFactory.CreateContext();

        var client = new Client { Name = "Acme", Slug = "acme" };
        var projectBackend = new Project { Name = "Backend", Slug = "backend", Client = client };
        var projectFrontend = new Project { Name = "Frontend", Slug = "frontend", Client = client };

        var repoApi = new Repository { Name = "api", RootPath = "/tmp/api", Project = projectBackend };
        var repoWorker = new Repository { Name = "worker", RootPath = "/tmp/worker", Project = projectBackend };
        var repoUi = new Repository { Name = "ui", RootPath = "/tmp/ui", Project = projectFrontend };

        ctx.Clients.Add(client);
        ctx.Projects.AddRange(projectBackend, projectFrontend);
        ctx.Repositories.AddRange(repoApi, repoWorker, repoUi);
        await ctx.SaveChangesAsync();

        var resolver = new ScopeResolver(ctx);

        var scoped = await resolver.ResolveScopeAsync(
            currentDirectory: "/tmp",
            repoNames: [],
            reposCsv: null,
            projectName: "Backend",
            clientName: null,
            allReposFlag: false,
            ct: CancellationToken.None);

        var names = scoped.Select(r => r.Name).OrderBy(n => n).ToArray();
        Assert.Equal(expectedArray, names);
    }

    [Fact]
    public async Task ClientScope_ReturnsAllReposForClient()
    {
        await using var ctx = SqliteTestContextFactory.CreateContext();

        var clientAcme = new Client { Name = "Acme", Slug = "acme" };
        var clientOther = new Client { Name = "Other", Slug = "other" };

        var projAcmeBackend = new Project { Name = "Backend", Slug = "backend", Client = clientAcme };
        var projOther = new Project { Name = "Proj", Slug = "proj", Client = clientOther };

        var repo1 = new Repository { Name = "api", RootPath = "/tmp/api", Project = projAcmeBackend };
        var repo2 = new Repository { Name = "worker", RootPath = "/tmp/worker", Project = projAcmeBackend };
        var repo3 = new Repository { Name = "other-repo", RootPath = "/tmp/other", Project = projOther };

        ctx.Clients.AddRange(clientAcme, clientOther);
        ctx.Projects.AddRange(projAcmeBackend, projOther);
        ctx.Repositories.AddRange(repo1, repo2, repo3);
        await ctx.SaveChangesAsync();

        var resolver = new ScopeResolver(ctx);

        var scoped = await resolver.ResolveScopeAsync(
            currentDirectory: "/tmp",
            repoNames: [],
            reposCsv: null,
            projectName: null,
            clientName: "Acme",
            allReposFlag: false,
            ct: CancellationToken.None);

        var names = scoped.Select(r => r.Name).OrderBy(n => n).ToArray();
        Assert.Equal(expectedArray0, names);
    }

    [Fact]
    public async Task NoFlags_InsideRepoWithProject_UsesProjectScope()
    {
        await using var ctx = SqliteTestContextFactory.CreateContext();

        // Arrange: one project with two repos
        var client = new Client { Name = "Acme", Slug = "acme" };
        var project = new Project { Name = "Backend", Slug = "backend", Client = client };

        var repoRoot1 = CreateTempDir("scope-tests-repo1");
        var repoRoot2 = CreateTempDir("scope-tests-repo2");

        var repo1 = new Repository { Name = "api", RootPath = repoRoot1, Project = project };
        var repo2 = new Repository { Name = "worker", RootPath = repoRoot2, Project = project };

        ctx.Clients.Add(client);
        ctx.Projects.Add(project);
        ctx.Repositories.AddRange(repo1, repo2);
        await ctx.SaveChangesAsync();

        var resolver = new ScopeResolver(ctx);

        // currentDirectory inside repo1
        var currentDir = Path.Combine(repoRoot1, "src");
        Directory.CreateDirectory(currentDir);

        var scoped = await resolver.ResolveScopeAsync(
            currentDirectory: currentDir,
            repoNames: [],
            reposCsv: null,
            projectName: null,
            clientName: null,
            allReposFlag: false,
            ct: CancellationToken.None);

        var names = scoped.Select(r => r.Name).OrderBy(n => n).ToArray();
        Assert.Equal(expectedArray1, names);
    }

    [Fact]
    public async Task NoFlags_InsideRepoWithoutProject_UsesOnlyThatRepo()
    {
        await using var ctx = SqliteTestContextFactory.CreateContext();

        var repoRoot = CreateTempDir("scope-tests-single");
        var repo = new Repository { Name = "solo-repo", RootPath = repoRoot, ProjectId = null };

        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();

        var resolver = new ScopeResolver(ctx);

        var currentDir = Path.Combine(repoRoot, "subdir");
        Directory.CreateDirectory(currentDir);

        var scoped = await resolver.ResolveScopeAsync(
            currentDirectory: currentDir,
            repoNames: [],
            reposCsv: null,
            projectName: null,
            clientName: null,
            allReposFlag: false,
            ct: CancellationToken.None);

        var single = Assert.Single(scoped);
        Assert.Equal("solo-repo", single.Name);
    }

    [Fact]
    public async Task NoFlags_NoActiveRepo_ThrowsWithGuidance()
    {
        await using var ctx = SqliteTestContextFactory.CreateContext();

        // Have at least one repo so we go into the "no active repo" branch
        var repo = new Repository { Name = "api", RootPath = "/tmp/api" };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();

        var resolver = new ScopeResolver(ctx);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveScopeAsync(
                currentDirectory: "/some/other/path",
                repoNames: [],
                reposCsv: null,
                projectName: null,
                clientName: null,
                allReposFlag: false,
                ct: CancellationToken.None));

        Assert.Contains("No active repository detected for current directory", ex.Message);
        Assert.Contains("Please specify scope explicitly", ex.Message);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string CreateTempDir(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "minorag-scope-tests", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}