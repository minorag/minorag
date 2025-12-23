namespace Minorag.Core.Models.ViewModels;

public record ProjectVm
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public required string Name { get; set; }
}
