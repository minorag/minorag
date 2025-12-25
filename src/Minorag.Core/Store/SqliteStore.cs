using Microsoft.EntityFrameworkCore;
using Minorag.Core.Models.Domain;
using Minorag.Core.Models.ViewModels;

namespace Minorag.Core.Store;

public interface ISqliteStore
{
    Task<RepositoryVm[]> GetRepositories(int?[] clientIds, int?[] projectIds, CancellationToken ct);
    Task<ClientVm[]> GetClients(CancellationToken ct);
    Task<ProjectVm[]> GetProjects(int[] clientIds, CancellationToken ct);
    Task<Repository?> GetRepositoryAsync(string repoRoot, CancellationToken ct);
    Task RemoveRepository(int repositoryId, CancellationToken ct);
    Task SetRepositoryLastIndexDate(int repoId, CancellationToken ct);
    Task<RepositoryFile?> GetFile(int repositoryId, string relPath, CancellationToken ct);
    Task CreateFile(RepositoryFile file, CancellationToken ct);
    Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct);
    Task<Dictionary<string, string>> GetFileHashesAsync(int repoId, CancellationToken ct);
    Task InsertChunkAsync(CodeChunk chunk, CancellationToken ct);
    Task DeleteChunksForFileAsync(int fileId, CancellationToken ct);
    Task DeleteChunksForFileAsync(int repoId, string relativePath, CancellationToken ct);
    IAsyncEnumerable<CodeChunkVm> GetAllChunksAsync(List<int>? repositoryIds = null, CancellationToken ct = default);
    Task SaveChanges(CancellationToken ct);
}

public class SqliteStore(RagDbContext db) : ISqliteStore
{
    public async Task<ClientVm[]> GetClients(CancellationToken ct)
    {
        var clients = await db.Clients
             .Select(x => new ClientVm
             {
                 Id = x.Id,
                 Name = x.Name,
             }).ToArrayAsync(ct);

        return clients;
    }

    public async Task<ProjectVm[]> GetProjects(int[] clientIds, CancellationToken ct)
    {
        var projectsQuery = db.Projects
              .Select(x => new ProjectVm
              {
                  Id = x.Id,
                  Name = x.Name,
                  ClientId = x.ClientId
              });

        if (clientIds.Length > 0)
        {
            projectsQuery = projectsQuery.Where(x => clientIds.Contains(x.ClientId));
        }

        var projects = await projectsQuery.ToArrayAsync(ct);
        return projects;
    }

    public async Task<RepositoryVm[]> GetRepositories(
     int?[] clientIds,
     int?[] projectIds,
     CancellationToken ct)
    {
        var clientIdSet = (clientIds ?? [])
            .Where(x => x.HasValue)
            .Distinct()
            .ToHashSet();

        var projectIdSet = (projectIds ?? [])
            .Where(x => x.HasValue)
            .Distinct()
            .ToHashSet();

        var q = db.Repositories
            .AsNoTracking()
            .Include(r => r.Project)
            .ThenInclude(p => p.Client)
            .AsQueryable();

        if (clientIdSet.Count > 0)
        {
            q = q.Where(r => clientIdSet.Contains(r.Project.ClientId));
        }

        if (projectIdSet.Count > 0)
        {
            q = q.Where(r => projectIdSet.Contains(r.ProjectId));
        }

        var repos = await q
            .Select(r => new RepositoryVm
            {
                Id = r.Id,
                Name = r.Name,
                RootPath = r.RootPath,
                LastIndexedAt = r.LastIndexedAt,
                ClientName = r.Project.Client.Name,
                ProjectName = r.Project.Name,

                // strongly recommended for frontend filtering
                ClientId = r.Project.ClientId,
                ProjectId = r.ProjectId
            })
            .ToListAsync(ct);

        return [.. repos
            .OrderBy(r => r.ClientName ?? string.Empty)
            .ThenBy(r => r.ProjectName ?? string.Empty)
            .ThenBy(r => r.Name)];
    }

    public async Task<Repository?> GetRepositoryAsync(string repoRoot, CancellationToken ct)
    {
        var repo = await db.Repositories
              .FirstOrDefaultAsync(r => r.RootPath == repoRoot, ct);
        return repo;
    }

    public async Task RemoveRepository(int repositoryId, CancellationToken ct)
    {
        using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Files
            .Where(c => c.RepositoryId == repositoryId)
            .ExecuteDeleteAsync(ct);

        await db.Repositories
            .Where(r => r.Id == repositoryId)
            .ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<RepositoryFile?> GetFile(int repositoryId, string relPath, CancellationToken ct)
    {
        var file = await db.Files
            .AsTracking()
            .FirstOrDefaultAsync(x => x.RepositoryId == repositoryId && x.Path == relPath, ct);

        return file;
    }

    public async Task CreateFile(RepositoryFile file, CancellationToken ct)
    {
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);
    }


    public async Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct)
    {
        repoRoot = Path.GetFullPath(repoRoot);

        var repo = await GetRepositoryAsync(repoRoot, ct);

        if (repo is not null)
        {
            return repo;
        }

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
        return await db.Files
            .Where(c => c.RepositoryId == repoId)
            .GroupBy(c => c.Path)
            .Select(g => new { g.Key, Hash = g.Select(c => c.FileHash).First() })
            .ToDictionaryAsync(
                x => x.Key,
                x => x.Hash,
                StringComparer.OrdinalIgnoreCase,
                ct);
    }

    public async Task DeleteChunksForFileAsync(
        int repoId,
        string relativePath,
        CancellationToken ct)
    {
        var file = await db.Files
            .Where(f => f.RepositoryId == repoId && f.Path == relativePath)
            .Select(f => new { f.Id })
            .SingleOrDefaultAsync(ct);

        if (file == null)
        {
            return;
        }

        await db.Files
            .Where(f => f.Id == file.Id)
            .ExecuteDeleteAsync(ct);

        await DeleteChunksForFileAsync(file.Id, ct);
    }

    public async Task DeleteChunksForFileAsync(int fileId, CancellationToken ct)
    {
        var toDelete = await db.Chunks
            .Where(c => c.FileId == fileId)
            .ToListAsync(ct);

        if (toDelete.Count == 0)
        {
            return;
        }

        db.Chunks.RemoveRange(toDelete);
        await db.SaveChangesAsync(ct);
    }

    public async Task InsertChunkAsync(CodeChunk chunk, CancellationToken ct)
    {
        db.Chunks.Add(chunk);
        await db.SaveChangesAsync(ct);
    }

    public IAsyncEnumerable<CodeChunkVm> GetAllChunksAsync(List<int>? repositoryIds = null, CancellationToken ct = default)
    {
        var baseQuery = db.Chunks.AsNoTracking();

        if (repositoryIds is not null && repositoryIds.Count > 0)
        {
            baseQuery = baseQuery.Where(x => repositoryIds.Contains(x.File.RepositoryId));
        }

        return baseQuery.Select(x => new CodeChunkVm
        {
            Id = x.Id,
            FileId = x.FileId,
            Embedding = x.Embedding,
            Extension = x.File.Extension,
            Language = x.File.Language,
            Kind = x.File.Kind,
            Path = x.File.Path,
            Content = x.Content,
            ChunkIndex = x.ChunkIndex
        }).AsAsyncEnumerable();
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

    public async Task SaveChanges(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}