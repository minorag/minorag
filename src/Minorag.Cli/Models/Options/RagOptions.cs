using Minorag.Cli.Configuration;

namespace Minorag.Cli.Models.Options;

public sealed record RagOptions
{
    /// <summary>
    /// Max characters per chunk when splitting documents.
    /// </summary>
    public int MaxChunkSize { get; init; } = DefaultValues.MaxChunkSize;

    public int MaxChunkTokens { get; init; } = 512;   // safe default for embeddings
    public int MaxChunkOverlapTokens { get; init; } = 40;

    /// <summary>
    /// Default number of chunks/snippets to retrieve per query.
    /// </summary>
    public int TopK { get; init; } = DefaultValues.TopK;
}
