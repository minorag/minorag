using Minorag.Core.Models.Domain;
using Minorag.Core.Models.ViewModels;
using Minorag.Core.Store;

namespace Minorag.Cli.Tests.TestInfrastructure;

public class BaseFakeStore : ISqliteStore
{
    public virtual Task CreateFile(RepositoryFile file, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task DeleteChunksForFileAsync(int repoId, string relativePath, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task DeleteChunksForFileAsync(int fileId, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual IAsyncEnumerable<CodeChunkVm> GetAllChunksAsync(List<int>? repositoryIds = null, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public virtual Task<ClientVm[]> GetClients(CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task<RepositoryFile?> GetFile(int repositoryId, string relPath, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task<Dictionary<string, string>> GetFileHashesAsync(int repoId, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task<ProjectVm[]> GetProjects(int[] clientIds, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public Task<RepositoryVm[]> GetRepositories(int?[] clientIds, int?[] projectIds, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task<Repository?> GetRepositoryAsync(string repoRoot, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task InsertChunkAsync(CodeChunk chunk, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task RemoveRepository(int repositoryId, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task SaveChanges(CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual Task SetRepositoryLastIndexDate(int repoId, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
