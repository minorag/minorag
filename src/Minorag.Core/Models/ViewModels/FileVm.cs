namespace Minorag.Core.Models.ViewModels;

public class FileVm
{
    public int Id { get; set; }
    public required string Path { get; set; }          // relative to the repo root
    public required string Extension { get; set; }
    public required string Language { get; set; }
    public string Kind { get; init; } = "file";
    public string? SymbolName { get; set; }
    public required string Content { get; set; }
    public required string FileHash { get; set; }
    public int RepositoryId { get; set; }
    public required CodeChunkVm[] CodeChunks { get; set; }
}
