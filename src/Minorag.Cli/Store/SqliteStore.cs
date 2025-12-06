using Microsoft.EntityFrameworkCore;
using Minorag.Cli.Models.Domain;

namespace Minorag.Cli.Store;

public interface ISqliteStore
{
    Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct);
    Task<Dictionary<string, string>> GetFileHashesAsync(int repoId, CancellationToken ct);
    Task InsertChunkAsync(CodeChunk chunk, CancellationToken ct);
    Task DeleteChunksForFileAsync(int repoId, string relativePath, CancellationToken ct);
    IAsyncEnumerable<CodeChunk> GetAllChunksAsync(bool verbose, CancellationToken ct);
}

public class SqliteStore(RagDbContext db) : ISqliteStore
{
    public async Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct)
    {
        repoRoot = Path.GetFullPath(repoRoot);

        var repo = await db.Repositories
            .FirstOrDefaultAsync(r => r.RootPath == repoRoot, ct);

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

    public IAsyncEnumerable<CodeChunk> GetAllChunksAsync(bool verbose, CancellationToken ct)
    {
        if (verbose)
        {
            return db.Chunks.AsNoTracking().Include(x => x.Repository).AsAsyncEnumerable();
        }

        return db.Chunks.AsNoTracking().AsAsyncEnumerable();
    }
}