namespace Minorag.Core.Models.ViewModels;

public class CodeChunkVm
{
    public long Id { get; init; }
    public required string Path { get; set; }
    public required string Language { get; set; }
    public required string Extension { get; set; }
    public required string Kind { get; set; }
    public required string Content { get; init; }
    public float[] Embedding { get; set; } = [];
    public int ChunkIndex { get; init; }
    public int FileId { get; set; }
}
