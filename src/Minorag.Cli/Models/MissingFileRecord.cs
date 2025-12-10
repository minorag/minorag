namespace Minorag.Cli.Models;

public sealed record MissingFileRecord(
    int RepositoryId,
    string RepositoryRoot,
    string RelativePath);
