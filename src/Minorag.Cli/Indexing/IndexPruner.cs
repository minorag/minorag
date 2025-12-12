using Microsoft.EntityFrameworkCore;
using Minorag.Cli.Models;
using Minorag.Cli.Store;

namespace Minorag.Cli.Indexing;

public interface IIndexPruner
{
    Task<PruneSummary> PruneAsync(
        bool dryRun,
        bool pruneOrphanOwners,
        CancellationToken ct);

    Task<DatabaseStatus> CalculateStatus(CancellationToken ct);
}

public sealed class IndexPruner(RagDbContext db, IFileSystemHelper fs) : IIndexPruner
{
    private const int MaxMissingFileSamples = 50;

    public async Task<DatabaseStatus> CalculateStatus(CancellationToken ct)
    {
        var totalRepos = await db.Repositories.CountAsync(ct);
        var totalChunks = await db.Chunks.CountAsync(ct);
        var totalClients = await db.Clients.CountAsync(ct);
        var totalProjects = await db.Projects.CountAsync(ct);
        var totalFiles = await db.Chunks
                                    .AsNoTracking()
                                    .Select(c => c.Path)
                                    .Distinct()
                                    .CountAsync(ct);


        var orphanedChunksQuery = from c in db.Chunks
                                  from r in db.Repositories.Where(x => x.Id == c.RepositoryId).DefaultIfEmpty()
                                  where r == null
                                  select c.Id;

        var orphanedChunks = await orphanedChunksQuery.CountAsync(ct);
        return new DatabaseStatus(totalClients, totalProjects, totalRepos, totalFiles, totalChunks, orphanedChunks);
    }

    public async Task<PruneSummary> PruneAsync(
        bool dryRun,
        bool pruneOrphanOwners,
        CancellationToken ct)
    {
        // -------------------------------------------------------------
        // Basic counts / empty index detection
        // -------------------------------------------------------------
        var status = await CalculateStatus(ct);

        if (status.TotalRepos == 0 && status.TotalChunks == 0)
        {
            return new PruneSummary(
                DatabaseMissing: false,
                IndexEmpty: true,
                TotalRepositories: status.TotalRepos,
                TotalChunks: status.TotalChunks,
                TotalClients: status.TotalClients,
                TotalProjects: status.TotalProjects,
                MissingRepositories: 0,
                OrphanedFileRecords: 0,
                OrphanChunks: status.OrphanedChunks,
                OrphanProjects: 0,
                OrphanClients: 0,
                MissingFileSamples: []);
        }

        // -------------------------------------------------------------
        // Repositories (existing vs missing on disk)
        // -------------------------------------------------------------
        var allRepos = await db.Repositories
            .AsNoTracking()
            .Select(r => new { r.Id, r.RootPath, r.ProjectId })
            .ToListAsync(ct);

        var missingRepos = allRepos
            .Where(r => string.IsNullOrWhiteSpace(r.RootPath) || !fs.DirectoryExists(r.RootPath))
            .ToList();

        var existingRepoRoots = allRepos
            .Where(r => !string.IsNullOrWhiteSpace(r.RootPath) && fs.DirectoryExists(r.RootPath))
            .ToDictionary(r => r.Id, r => r.RootPath!);

        // -------------------------------------------------------------
        // Files that no longer exist on disk (for existing repos)
        // -------------------------------------------------------------
        var chunkInfos = await db.Chunks
            .AsNoTracking()
            .Select(c => new { c.Id, c.RepositoryId, c.Path })
            .ToListAsync(ct);

        var missingFileChunkIds = new List<long>();
        var missingFileRecords = new HashSet<(int RepoId, string Path)>();
        var missingFileSamples = new List<MissingFileRecord>();

        foreach (var c in chunkInfos)
        {
            if (!existingRepoRoots.TryGetValue(c.RepositoryId, out var rootPath))
            {
                continue;
            }

            var fullPath = Path.Combine(rootPath, c.Path);
            if (!fs.FileExists(fullPath))
            {
                missingFileChunkIds.Add(c.Id);
                missingFileRecords.Add((c.RepositoryId, c.Path));

                if (missingFileSamples.Count < MaxMissingFileSamples)
                {
                    missingFileSamples.Add(
                        new MissingFileRecord(
                            RepositoryId: c.RepositoryId,
                            RepositoryRoot: rootPath,
                            RelativePath: c.Path));
                }
            }
        }

        var orphanedFileRecordsCount = missingFileRecords.Count;

        // -------------------------------------------------------------
        // Orphan projects / clients (no repos / no projects)
        // -------------------------------------------------------------
        var allProjects = await db.Projects
            .AsNoTracking()
            .Select(p => new { p.Id, p.ClientId })
            .ToListAsync(ct);

        var repoProjectIds = allRepos
            .Where(r => r.ProjectId != null)
            .Select(r => r.ProjectId!.Value)
            .Distinct()
            .ToHashSet();

        var orphanProjectIds = allProjects
            .Where(p => !repoProjectIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToList();

        var allClients = await db.Clients
            .AsNoTracking()
            .Select(c => new { c.Id })
            .ToListAsync(ct);

        var projectClientIds = allProjects
            .Select(p => p.ClientId)
            .Distinct()
            .ToHashSet();

        var orphanClientIds = allClients
            .Where(c => !projectClientIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToList();

        var orphanProjectsCount = orphanProjectIds.Count;
        var orphanClientsCount = orphanClientIds.Count;

        if (!dryRun)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            if (missingFileChunkIds.Count > 0)
            {
                await db.Chunks
                    .Where(c => missingFileChunkIds.Contains(c.Id))
                    .ExecuteDeleteAsync(ct);
            }

            if (missingRepos.Count > 0)
            {
                var missingRepoIds = missingRepos.Select(r => r.Id).ToList();

                await db.Chunks
                    .Where(c => missingRepoIds.Contains(c.RepositoryId))
                    .ExecuteDeleteAsync(ct);

                await db.Repositories
                    .Where(r => missingRepoIds.Contains(r.Id))
                    .ExecuteDeleteAsync(ct);
            }

            if (status.OrphanedChunks > 0)
            {
                var orphanChunkIdsQuery =
                    from c in db.Chunks
                    join r in db.Repositories on c.RepositoryId equals r.Id into repos
                    from r in repos.DefaultIfEmpty()
                    where r == null
                    select c.Id;

                // ExecuteDeleteAsync supports subqueries; avoid loading IDs into memory.
                await db.Chunks
                    .Where(c => orphanChunkIdsQuery.Contains(c.Id))
                    .ExecuteDeleteAsync(ct);
            }


            if (pruneOrphanOwners)
            {
                if (orphanProjectIds.Count > 0)
                {
                    await db.Projects
                        .Where(p => orphanProjectIds.Contains(p.Id))
                        .ExecuteDeleteAsync(ct);
                }

                if (orphanClientIds.Count > 0)
                {
                    await db.Clients
                        .Where(c => orphanClientIds.Contains(c.Id))
                        .ExecuteDeleteAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
        }

        return new PruneSummary(
            DatabaseMissing: false,
            IndexEmpty: false,
            TotalRepositories: status.TotalRepos,
            TotalChunks: status.TotalChunks,
            TotalClients: status.TotalClients,
            TotalProjects: status.TotalProjects,
            MissingRepositories: missingRepos.Count,
            OrphanedFileRecords: orphanedFileRecordsCount,
            OrphanChunks: status.OrphanedChunks,
            OrphanProjects: orphanProjectsCount,
            OrphanClients: orphanClientsCount,
            MissingFileSamples: missingFileSamples);
    }
}