# nullable disable

using System.ComponentModel.DataAnnotations;

namespace Minorag.Core.Models.Domain;

public class Project
{
    [Key]
    public int Id { get; init; }
    public int ClientId { get; init; }
    public string Name { get; init; } = null!;
    public string Slug { get; init; } = null!;
    public virtual Client Client { get; set; }
    public virtual IList<Repository> Repositories { get; set; }
}