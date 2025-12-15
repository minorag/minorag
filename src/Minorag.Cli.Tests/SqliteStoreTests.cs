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
        // Arrange
        var (db, store) = CreateStore();
        using var _ = db;

        // Act
        var repo = await store.GetRepositoryAsync(NormalizePath("/tmp/does-not-exist"), CancellationToken.None);

        // Assert
        Assert.Null(repo);
    }

    [Fact]
    public async Task GetRepositoryAsync_Found_ReturnsRepository()
    {
        // Arrange
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

        // Act
        var result = await store.GetRepositoryAsync(rootPath, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(rootPath, result!.RootPath);
        Assert.Equal("repo1", result.Name);
    }

    [Fact]
    public async Task GetOrCreateRepositoryAsync_Creates_WhenNotExists()
    {
        // Arrange
        var (db, store) = CreateStore();
        using var _ = db;

        var rootPath = NormalizePath("/tmp/new-repo");

        // Act
        var repo = await store.GetOrCreateRepositoryAsync(rootPath, CancellationToken.None);

        // Assert
        Assert.NotNull(repo);
        Assert.True(repo.Id > 0);
        Assert.Equal(rootPath, repo.RootPath);
        Assert.Equal("new-repo", repo.Name); // Path.GetFileName(rootPath)
    }

    [Fact]
    public async Task GetOrCreateRepositoryAsync_ReturnsExisting_WhenAlreadyThere()
    {
        // Arrange
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

        // Act
        var repo = await store.GetOrCreateRepositoryAsync(rootPath, CancellationToken.None);

        // Assert
        Assert.Equal(existing.Id, repo.Id);
        Assert.Equal("existing-repo", repo.Name);
        Assert.Equal(rootPath, repo.RootPath);
    }

    [Fact]
    public async Task RemoveRepository_RemovesRepoAndAssociatedChunks()
    {
        // Arrange
        var (db, store) = CreateStore();
        using var _ = db;

        var repo = new Repository
        {
            RootPath = NormalizePath("/tmp/repo-to-remove"),
            Name = "repo-to-remove"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        db.Chunks.AddRange(
            CreateChunk(repo.Id, "src/code1.cs", chunkIndex: 0, fileHash: "H1"),
            CreateChunk(repo.Id, "src/code2.cs", chunkIndex: 1, fileHash: "H2")
        );

        var otherRepo = new Repository
        {
            RootPath = NormalizePath("/tmp/other-repo"),
            Name = "other-repo"
        };

        db.Repositories.Add(otherRepo);
        await db.SaveChangesAsync();

        db.Chunks.Add(
            CreateChunk(otherRepo.Id, "src/other.cs", chunkIndex: 0, fileHash: "H3")
        );

        await db.SaveChangesAsync();

        // Act
        await store.RemoveRepository(repo.Id, CancellationToken.None);

        // Assert
        var repos = await db.Repositories.ToListAsync();
        Assert.DoesNotContain(repos, r => r.Id == repo.Id);
        Assert.Contains(repos, r => r.Id == otherRepo.Id);

        var chunks = await db.Chunks.ToListAsync();
        Assert.DoesNotContain(chunks, c => c.RepositoryId == repo.Id);
        Assert.Contains(chunks,
            c => c.RepositoryId == otherRepo.Id &&
          NormalizePath(c.Path).EndsWith("src/other.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RemoveRepository_IsIdempotent_WhenRepoDoesNotExist()
    {
        // Arrange
        var (db, store) = CreateStore();
        using var _ = db;

        // Act & Assert: should not throw if repo is already gone / never existed
        await store.RemoveRepository(repositoryId: 123456, CancellationToken.None);
    }

    [Fact]
    public async Task SetRepositoryLastIndexDate_SetsUtcTimestamp()
    {
        // Arrange
        var (db, store) = CreateStore();
        using var _ = db;

        var repo = new Repository
        {
            RootPath = NormalizePath("/tmp/repo-last-index"),
            Name = "repo-last-index"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        // Act
        await store.SetRepositoryLastIndexDate(repo.Id, CancellationToken.None);

        // Assert
        var updated = await db.Repositories.FirstAsync(r => r.Id == repo.Id);
        Assert.NotNull(updated.LastIndexedAt);

        var now = DateTime.UtcNow;
        var diff = now - updated.LastIndexedAt!.Value;

        // Should be reasonably recent
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
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Creates a CodeChunk shaped similarly to what Indexer writes.
    /// Adjust fields if your CodeChunk has more required properties.
    /// </summary>
    private static CodeChunk CreateChunk(
        int repositoryId,
        string relativePath,
        int chunkIndex,
        string fileHash)
    {
        return new CodeChunk
        {
            RepositoryId = repositoryId,
            Path = NormalizePath(relativePath),
            Extension = Path.GetExtension(relativePath).TrimStart('.'),
            Language = "csharp",
            ChunkIndex = chunkIndex,
            FileHash = fileHash,
            Content = $"// chunk {chunkIndex} for {relativePath}"
        };
    }
}