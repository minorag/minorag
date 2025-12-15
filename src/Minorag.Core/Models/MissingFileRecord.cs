namespace Minorag.Core.Models
{
    public sealed record MissingFileRecord(
        int RepositoryId,
        string RepositoryRoot,
        string RelativePath);
}
