namespace Minorag.Core.Models.ViewModels;

public class RepositoryVm
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string RootPath { get; set; }
    public string? ClientName { get; set; }
    public string? ProjectName { get; set; }
    public DateTime? LastIndexedAt { get; set; }
}
