namespace Minorag.Cli.Models.Options;

public sealed record RagOptions
{
    /// <summary>
    /// Max characters per chunk when splitting documents.
    /// </summary>
    public int MaxChunkSize { get; init; } = 2000;

    /// <summary>
    /// Default number of chunks/snippets to retrieve per query.
    /// </summary>
    public int TopK { get; init; } = 7;
}
