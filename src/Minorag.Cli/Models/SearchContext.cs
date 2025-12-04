namespace Minorag.Cli.Models;

public sealed record SearchContext(string Question, IReadOnlyList<ScoredChunk> Chunks)
{
    public bool HasResults => Chunks.Count > 0;
}
