#nullable disable

namespace Minorag.Core.Models.Domain;

public class Repository
{
    public int Id { get; set; }

    public int? ProjectId { get; set; }

    public string RootPath { get; set; } = default!;

    public string Name { get; set; } = default!;

    public DateTime? LastIndexedAt { get; set; }

    public virtual Project Project { get; set; }

    public virtual ICollection<CodeChunk> Chunks { get; set; } = [];
}
