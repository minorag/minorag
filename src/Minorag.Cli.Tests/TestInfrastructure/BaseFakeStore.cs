using Minorag.Core.Models.Domain;
using Minorag.Core.Store;

namespace Minorag.Cli.Tests.TestInfrastructure;

public class BaseFakeStore : ISqliteStore
{
    public virtual Task DeleteChunksForFileAsync(int repoId, string relativePath, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public virtual IAsyncEnumerable<CodeChunk> GetAllChunksAsync(bool verbose, List<int>? repositoryIds = null, CancellationToken ct = default)
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

    public Task<Repository?> GetRepositoryAsync(string repoRoot, CancellationToken ct)
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

    public virtual Task SetRepositoryLastIndexDate(int repoId, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
