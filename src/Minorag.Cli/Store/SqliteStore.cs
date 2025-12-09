using Microsoft.EntityFrameworkCore;
using Minorag.Cli.Models.Domain;

namespace Minorag.Cli.Store;

public interface ISqliteStore
{
    Task<Repository?> GetRepositoryAsync(string repoRoot, CancellationToken ct);
    Task RemoveRepository(int repositoryId, CancellationToken ct);
    Task SetRepositoryLastIndexDate(int repoId, CancellationToken ct);
    Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct);
    Task<Dictionary<string, string>> GetFileHashesAsync(int repoId, CancellationToken ct);
    Task InsertChunkAsync(CodeChunk chunk, CancellationToken ct);
    Task DeleteChunksForFileAsync(int repoId, string relativePath, CancellationToken ct);
    IAsyncEnumerable<CodeChunk> GetAllChunksAsync(bool verbose, List<int>? repositoryIds = null, CancellationToken ct = default);
}

public class SqliteStore(RagDbContext db) : ISqliteStore
{
    public async Task<Repository?> GetRepositoryAsync(string repoRoot, CancellationToken ct)
    {
        var repo = await db.Repositories
              .FirstOrDefaultAsync(r => r.RootPath == repoRoot, ct);
        return repo;
    }

    public async Task RemoveRepository(int repositoryId, CancellationToken ct)
    {
        using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Chunks
            .Where(c => c.RepositoryId == repositoryId)
            .ExecuteDeleteAsync(ct);

        await db.Repositories
            .Where(r => r.Id == repositoryId)
            .ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct)
    {
        repoRoot = Path.GetFullPath(repoRoot);

        var repo = await GetRepositoryAsync(repoRoot, ct);

        if (repo is not null)
            return repo;

        repo = new Repository
        {
            RootPath = repoRoot,
            Name = Path.GetFileName(repoRoot)
        };

        db.Repositories.Add(repo);
        await db.SaveChangesAsync(ct);

        return repo;
    }

    public async Task<Dictionary<string, string>> GetFileHashesAsync(int repoId, CancellationToken ct)
    {
        return await db.Chunks
            .Where(c => c.RepositoryId == repoId)
            .GroupBy(c => c.Path)
            .Select(g => new { g.Key, Hash = g.Select(c => c.FileHash).First() })
            .ToDictionaryAsync(
                x => x.Key,
                x => x.Hash,
                StringComparer.OrdinalIgnoreCase,
                ct);
    }

    public async Task DeleteChunksForFileAsync(int repoId, string relativePath, CancellationToken ct)
    {
        var toDelete = await db.Chunks
            .Where(c => c.RepositoryId == repoId && c.Path == relativePath)
            .ToListAsync(ct);

        if (toDelete.Count == 0)
            return;

        db.Chunks.RemoveRange(toDelete);
        await db.SaveChangesAsync(ct);
    }

    public async Task InsertChunkAsync(CodeChunk chunk, CancellationToken ct)
    {
        db.Chunks.Add(chunk);
        await db.SaveChangesAsync(ct);
    }

    public IAsyncEnumerable<CodeChunk> GetAllChunksAsync(bool verbose, List<int>? repositoryIds = null, CancellationToken ct = default)
    {
        var baseQuery = db.Chunks.AsNoTracking();

        if (repositoryIds is not null && repositoryIds.Count > 0)
        {
            baseQuery = baseQuery.Where(x => repositoryIds.Contains(x.RepositoryId));
        }

        if (verbose)
        {
            baseQuery = baseQuery.Include(x => x.Repository);
        }


        return baseQuery.AsAsyncEnumerable();
    }

    public async Task SetRepositoryLastIndexDate(int repoId, CancellationToken ct)
    {
        var repo = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repoId, ct);
        if (repo is not null)
        {
            repo.LastIndexedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}