#nullable disable
namespace Minorag.Cli.Models.Domain;

public class CodeChunk
{
    public long Id { get; init; }
    public required string Path { get; init; }
    public required string Extension { get; init; }
    public required string Language { get; init; }
    public string Kind { get; init; } = "file"; // file / type / method / etc.
    public string SymbolName { get; init; }
    public required string Content { get; init; }
    public float[] Embedding { get; set; } = [];
    public required string FileHash { get; init; }
    public int ChunkIndex { get; init; }

    public int RepositoryId { get; set; }
    public virtual Repository Repository { get; set; }
}