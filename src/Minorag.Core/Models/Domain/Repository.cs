#nullable disable

using System.ComponentModel.DataAnnotations;

namespace Minorag.Core.Models.Domain;

public class Repository
{
    [Key]
    public int Id { get; set; }

    public int? ProjectId { get; set; }

    public string RootPath { get; set; } = default!;

    public string Name { get; set; } = default!;

    public DateTime? LastIndexedAt { get; set; }

    public virtual Project Project { get; set; }

    public virtual ICollection<RepositoryFile> Files { get; set; } = [];
}
