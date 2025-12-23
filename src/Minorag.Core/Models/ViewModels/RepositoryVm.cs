namespace Minorag.Core.Models.ViewModels;

public record RepositoryVm
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string RootPath { get; set; }
    public int? ClientId { get; set; }
    public int? ProjectId { get; set; }
    public string? ClientName { get; set; }
    public string? ProjectName { get; set; }
    public DateTime? LastIndexedAt { get; set; }
}
