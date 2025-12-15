namespace Minorag.Core.Models;

public sealed record SearchContext(string Question, IReadOnlyList<ScoredChunk> Chunks)
{
    public bool UseAdvancedModel { get; set; }
    public bool HasResults => Chunks.Count > 0;
}
