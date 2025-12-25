#nullable disable

using System.ComponentModel.DataAnnotations;

namespace Minorag.Core.Models.Domain;

public class CodeChunk
{
    [Key]
    public long Id { get; init; }
    public required string Content { get; init; }
    public float[] Embedding { get; set; } = [];
    public int ChunkIndex { get; init; }

    public int FileId { get; set; }

    public virtual RepositoryFile File { get; set; }
}