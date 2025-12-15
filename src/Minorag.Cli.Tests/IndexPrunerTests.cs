using Microsoft.EntityFrameworkCore;
using Minorag.Cli.Tests.TestInfrastructure;
using Minorag.Core.Indexing;
using Minorag.Core.Models.Domain;
using Minorag.Core.Services;

namespace Minorag.Cli.Tests;

public class IndexPrunerTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "minorag-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task PruneAsync_EmptyIndex_ReturnsIndexEmptySummary()
    {
        using var db = SqliteTestContextFactory.CreateContext();
        var fs = new FileSystemHelper();

        var pruner = new IndexPruner(db, fs);

        var summary = await pruner.PruneAsync(
            dryRun: true,
            pruneOrphanOwners: false,
            ct: CancellationToken.None);

        Assert.True(summary.IndexEmpty);
        Assert.False(summary.DatabaseMissing);
        Assert.Equal(0, summary.TotalRepositories);
        Assert.Equal(0, summary.TotalChunks);
        Assert.Equal(0, summary.TotalClients);
        Assert.Equal(0, summary.TotalProjects);
        Assert.Equal(0, summary.MissingRepositories);
        Assert.Equal(0, summary.OrphanedFileRecords);
        Assert.Equal(0, summary.OrphanProjects);
        Assert.Equal(0, summary.OrphanClients);
        Assert.Empty(summary.MissingFileSamples);
    }

    [Fact]
    public async Task PruneAsync_ExistingRepoAndFile_HasNoMissingRecords()
    {
        using var db = SqliteTestContextFactory.CreateContext();

        // Arrange: client → project → repo with existing root + file
        var repoRoot = CreateTempDirectory();
        var fileRelativePath = "src/Program.cs";
        var fileFullPath = Path.Combine(repoRoot, fileRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fileFullPath)!);
        await File.WriteAllTextAsync(fileFullPath, "// test file");

        var client = new Client
        {
            Name = "Client A",
            Slug = "client-a"
        };

        var project = new Project
        {
            Name = "Project A",
            Slug = "project-a",
            Client = client
        };

        var repo = new Repository
        {
            Name = "Repo A",
            RootPath = repoRoot,
            Project = project
        };

        db.Clients.Add(client);
        db.Projects.Add(project);
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var chunk = TestChunkFactory.CreateChunk(
            id: 1,
            embedding: [],
            repositoryId: repo.Id,
            path: fileRelativePath);

        db.Chunks.Add(chunk);
        await db.SaveChangesAsync();
        var fs = new FileSystemHelper();

        var pruner = new IndexPruner(db, fs);

        // Act
        var summary = await pruner.PruneAsync(
            dryRun: true,
            pruneOrphanOwners: false,
            ct: CancellationToken.None);

        // Assert
        Assert.False(summary.IndexEmpty);
        Assert.Equal(1, summary.TotalRepositories);
        Assert.Equal(1, summary.TotalChunks);
        Assert.Equal(1, summary.TotalClients);
        Assert.Equal(1, summary.TotalProjects);

        Assert.Equal(0, summary.MissingRepositories);
        Assert.Equal(0, summary.OrphanedFileRecords);
        Assert.Empty(summary.MissingFileSamples);
    }

    [Fact]
    public async Task PruneAsync_MissingFile_ReportsAndDeletesChunksWhenNotDryRun()
    {
        using var db = SqliteTestContextFactory.CreateContext();

        // Arrange: repo root exists, but the indexed file is missing
        var repoRoot = CreateTempDirectory();
        var fileRelativePath = "src/Missing.cs";

        var client = new Client { Name = "Client", Slug = "client" };
        var project = new Project { Name = "Project", Slug = "project", Client = client };
        var repo = new Repository { Name = "Repo", RootPath = repoRoot, Project = project };

        db.Clients.Add(client);
        db.Projects.Add(project);
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var chunk = TestChunkFactory.CreateChunk(
            id: 1,
            embedding: [],
            repositoryId: repo.Id,
            path: fileRelativePath);

        db.Chunks.Add(chunk);
        await db.SaveChangesAsync();
        var fs = new FileSystemHelper();

        var pruner = new IndexPruner(db, fs);

        // Act 1: dry-run
        var drySummary = await pruner.PruneAsync(
            dryRun: true,
            pruneOrphanOwners: false,
            ct: CancellationToken.None);

        // Assert: reported but not deleted
        Assert.Equal(1, drySummary.OrphanedFileRecords);
        Assert.Single(drySummary.MissingFileSamples);
        var sample = drySummary.MissingFileSamples.Single();
        Assert.Equal(repo.Id, sample.RepositoryId);
        Assert.Equal(repoRoot, sample.RepositoryRoot);
        Assert.Equal(fileRelativePath, sample.RelativePath);

        var chunkCountBefore = await db.Chunks.LongCountAsync();
        Assert.Equal(1, chunkCountBefore);

        // Act 2: actually prune
        var liveSummary = await pruner.PruneAsync(
            dryRun: false,
            pruneOrphanOwners: false,
            ct: CancellationToken.None);

        // Assert: chunk deleted, repo kept
        var chunkCountAfter = await db.Chunks.LongCountAsync();
        var repoCountAfter = await db.Repositories.LongCountAsync();

        Assert.Equal(0, chunkCountAfter);
        Assert.Equal(1, repoCountAfter);

        // Summary still reflects pre-prune state
        Assert.Equal(1, liveSummary.OrphanedFileRecords);
    }

    [Fact]
    public async Task PruneAsync_MissingRepositoryDirectory_RemovesRepoAndChunks()
    {
        using var db = SqliteTestContextFactory.CreateContext();

        // Arrange: repo root points to non-existent directory
        var nonExistingRoot = Path.Combine(
            Path.GetTempPath(),
            "minorag-tests",
            "missing-" + Guid.NewGuid().ToString("N"));

        var client = new Client { Name = "Client", Slug = "client" };
        var project = new Project { Name = "Project", Slug = "project", Client = client };
        var repo = new Repository { Name = "Repo", RootPath = nonExistingRoot, Project = project };

        db.Clients.Add(client);
        db.Projects.Add(project);
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var chunk = TestChunkFactory.CreateChunk(
            id: 1,
            embedding: Array.Empty<float>(),
            repositoryId: repo.Id,
            path: "src/Whatever.cs");

        db.Chunks.Add(chunk);
        await db.SaveChangesAsync();

        var fs = new FileSystemHelper();

        var pruner = new IndexPruner(db, fs);

        // Act 1: dry-run
        var drySummary = await pruner.PruneAsync(
            dryRun: true,
            pruneOrphanOwners: false,
            ct: CancellationToken.None);

        // Assert: repo reported as missing, but file not counted as "missing file record"
        Assert.Equal(1, drySummary.MissingRepositories);
        Assert.Equal(0, drySummary.OrphanedFileRecords);
        Assert.Empty(drySummary.MissingFileSamples);

        // Act 2: live prune
        var liveSummary = await pruner.PruneAsync(
            dryRun: false,
            pruneOrphanOwners: false,
            ct: CancellationToken.None);

        // Assert: repo and its chunks are removed
        var repoCountAfter = await db.Repositories.LongCountAsync();
        var chunkCountAfter = await db.Chunks.LongCountAsync();

        Assert.Equal(0, repoCountAfter);
        Assert.Equal(0, chunkCountAfter);

        Assert.Equal(1, liveSummary.MissingRepositories);
    }

    [Fact]
    public async Task PruneAsync_PruneOrphanOwners_RemovesProjectsAndClientsWithoutRepos()
    {
        using var db = SqliteTestContextFactory.CreateContext();
        var fs = new FileSystemHelper();
        // Arrange:
        // client1 -> project1 -> repo1 (valid)
        // client2 -> project2 (no repos)
        // client3 (no projects)
        var repoRoot = CreateTempDirectory();

        var client1 = new Client { Name = "Client1", Slug = "client1" };
        var project1 = new Project { Name = "Project1", Slug = "project1", Client = client1 };
        var repo1 = new Repository { Name = "Repo1", RootPath = repoRoot, Project = project1 };

        var client2 = new Client { Name = "Client2", Slug = "client2" };
        var project2 = new Project { Name = "Project2", Slug = "project2", Client = client2 };

        var client3 = new Client { Name = "Client3", Slug = "client3" };

        db.Clients.AddRange(client1, client2, client3);
        db.Projects.AddRange(project1, project2);
        db.Repositories.Add(repo1);
        await db.SaveChangesAsync();

        var chunk = TestChunkFactory.CreateChunk(
            id: 1,
            embedding: [],
            repositoryId: repo1.Id,
            path: "src/Program.cs");

        db.Chunks.Add(chunk);
        await db.SaveChangesAsync();

        var pruner = new IndexPruner(db, fs);

        // Act: prune orphan owners
        var summary = await pruner.PruneAsync(
            dryRun: false,
            pruneOrphanOwners: true,
            ct: CancellationToken.None);

        // Assert: project2 & client3 should be considered orphan
        Assert.Equal(1, summary.OrphanProjects);
        Assert.Equal(1, summary.OrphanClients);

        var projects = await db.Projects.AsNoTracking().ToListAsync();
        var clients = await db.Clients.AsNoTracking().ToListAsync();

        Assert.Contains(projects, p => p.Id == project1.Id);
        Assert.DoesNotContain(projects, p => p.Id == project2.Id);

        Assert.Contains(clients, c => c.Id == client1.Id);
        // client2 is still referenced by project2 *before* prune; depending on semantics
        // you may or may not want it to be removed. Current pruner only looks at projectClientIds.
        Assert.Contains(clients, c => c.Id == client2.Id);
        Assert.DoesNotContain(clients, c => c.Id == client3.Id);
    }
}