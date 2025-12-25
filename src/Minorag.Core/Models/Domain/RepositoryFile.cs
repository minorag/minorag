#nullable disable

using System.ComponentModel.DataAnnotations;

namespace Minorag.Core.Models.Domain;

public class RepositoryFile
{
    [Key]
    public int Id { get; set; }
    public required string Path { get; set; }          // relative to the repo root
    public required string Extension { get; set; }
    public required string Language { get; set; }
    public string Kind { get; init; } = "file";
    public string SymbolName { get; set; }
    public required string Content { get; set; }
    public required string FileHash { get; set; }
    public int RepositoryId { get; set; }// this is the repo path

    public virtual Repository Repository { get; set; }
    public virtual ICollection<CodeChunk> Chunks { get; set; }
}
