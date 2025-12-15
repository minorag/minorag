namespace Minorag.Core.Models;

public sealed record SearchResult(string Question, IReadOnlyList<ScoredChunk> Chunks, string? Answer)
{
    public bool HasAnswer => !string.IsNullOrWhiteSpace(Answer);
    public bool HasResults => Chunks.Count > 0;
}