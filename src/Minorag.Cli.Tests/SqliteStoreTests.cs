using Microsoft.EntityFrameworkCore;
using Minorag.Cli.Tests.TestInfrastructure;
using Minorag.Core.Models.Domain;
using Minorag.Core.Store;

namespace Minorag.Cli.Tests;

public class SqliteStoreTests
{
    [Fact]
    public async Task GetRepositoryAsync_NotFound_ReturnsNull()
    {
        var (db, store) = CreateStore();
        using var _ = db;

        var repo = await store.GetRepositoryAsync(NormalizePath("/tmp/does-not-exist"), CancellationToken.None);

        Assert.Null(repo);
    }

    [Fact]
    public async Task GetRepositoryAsync_Found_ReturnsRepository()
    {
        var (db, store) = CreateStore();
        using var _ = db;

        var rootPath = NormalizePath("/tmp/repo1");
        var repo = new Repository
        {
            RootPath = rootPath,
            Name = "repo1"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var result = await store.GetRepositoryAsync(rootPath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(rootPath, result!.RootPath);
        Assert.Equal("repo1", result.Name);
    }

    [Fact]
    public async Task GetOrCreateRepositoryAsync_Creates_WhenNotExists()
    {
        var (db, store) = CreateStore();
        using var _ = db;

        var rootPath = NormalizePath("/tmp/new-repo");

        var repo = await store.GetOrCreateRepositoryAsync(rootPath, CancellationToken.None);

        Assert.NotNull(repo);
        Assert.True(repo.Id > 0);
        Assert.Equal(rootPath, repo.RootPath);
        Assert.Equal("new-repo", repo.Name);
    }

    [Fact]
    public async Task GetOrCreateRepositoryAsync_ReturnsExisting_WhenAlreadyThere()
    {
        var (db, store) = CreateStore();
        using var _ = db;

        var rootPath = NormalizePath("/tmp/existing-repo");
        var existing = new Repository
        {
            RootPath = rootPath,
            Name = "existing-repo"
        };
        db.Repositories.Add(existing);
        await db.SaveChangesAsync();

        var repo = await store.GetOrCreateRepositoryAsync(rootPath, CancellationToken.None);

        Assert.Equal(existing.Id, repo.Id);
        Assert.Equal("existing-repo", repo.Name);
        Assert.Equal(rootPath, repo.RootPath);
    }

    [Fact]
    public async Task RemoveRepository_RemovesRepoAndAssociatedChunks()
    {
        var (db, store) = CreateStore();
        using var _ = db;

        var repo = new Repository
        {
            RootPath = NormalizePath("/tmp/repo-to-remove"),
            Name = "repo-to-remove"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var file1 = CreateFile(repo.Id, "src/code1.cs", fileHash: "H1");
        var file2 = CreateFile(repo.Id, "src/code2.cs", fileHash: "H2");
        db.Files.AddRange(file1, file2);
        await db.SaveChangesAsync();

        db.Chunks.AddRange(
            CreateChunk(file1.Id, chunkIndex: 0, relativePathForContent: file1.Path),
            CreateChunk(file2.Id, chunkIndex: 1, relativePathForContent: file2.Path)
        );

        var otherRepo = new Repository
        {
            RootPath = NormalizePath("/tmp/other-repo"),
            Name = "other-repo"
        };
        db.Repositories.Add(otherRepo);
        await db.SaveChangesAsync();

        var otherFile = CreateFile(otherRepo.Id, "src/other.cs", fileHash: "H3");
        db.Files.Add(otherFile);
        await db.SaveChangesAsync();

        db.Chunks.Add(CreateChunk(otherFile.Id, chunkIndex: 0, relativePathForContent: otherFile.Path));

        await db.SaveChangesAsync();

        await store.RemoveRepository(repo.Id, CancellationToken.None);

        var repos = await db.Repositories.ToListAsync();
        Assert.DoesNotContain(repos, r => r.Id == repo.Id);
        Assert.Contains(repos, r => r.Id == otherRepo.Id);

        var files = await db.Files.ToListAsync();
        Assert.DoesNotContain(files, f => f.RepositoryId == repo.Id);
        Assert.Contains(files, f => f.RepositoryId == otherRepo.Id);

        var chunks = await db.Chunks.ToListAsync();
        Assert.DoesNotContain(chunks, c => c.File.RepositoryId == repo.Id);

        Assert.Contains(chunks, c =>
            c.File.RepositoryId == otherRepo.Id &&
            NormalizePath(c.File.Path).EndsWith("src/other.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RemoveRepository_IsIdempotent_WhenRepoDoesNotExist()
    {
        var (db, store) = CreateStore();
        using var _ = db;

        await store.RemoveRepository(repositoryId: 123456, CancellationToken.None);
    }

    [Fact]
    public async Task SetRepositoryLastIndexDate_SetsUtcTimestamp()
    {
        var (db, store) = CreateStore();
        using var _ = db;

        var repo = new Repository
        {
            RootPath = NormalizePath("/tmp/repo-last-index"),
            Name = "repo-last-index"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        await store.SetRepositoryLastIndexDate(repo.Id, CancellationToken.None);

        var updated = await db.Repositories.FirstAsync(r => r.Id == repo.Id);
        Assert.NotNull(updated.LastIndexedAt);

        var now = DateTime.UtcNow;
        var diff = now - updated.LastIndexedAt!.Value;
        Assert.True(diff.TotalMinutes < 1);
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private static (RagDbContext db, SqliteStore store) CreateStore()
    {
        var db = SqliteTestContextFactory.CreateContext();
        var store = new SqliteStore(db);
        return (db, store);
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private static RepositoryFile CreateFile(int repositoryId, string relativePath, string fileHash)
    {
        var ext = Path.GetExtension(relativePath).TrimStart('.');
        return new RepositoryFile
        {
            RepositoryId = repositoryId,
            Path = NormalizePath(relativePath),
            Extension = ext,
            Language = "csharp",
            Kind = "file",
            SymbolName = null,
            Content = $"// file content for {relativePath}",
            FileHash = fileHash
        };
    }

    private static CodeChunk CreateChunk(int fileId, int chunkIndex, string relativePathForContent)
    {
        return new CodeChunk
        {
            FileId = fileId,
            ChunkIndex = chunkIndex,
            Content = $"// chunk {chunkIndex} for {relativePathForContent}",
            Embedding = []
        };
    }
}