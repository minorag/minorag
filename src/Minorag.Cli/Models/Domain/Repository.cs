namespace Minorag.Cli.Models.Domain;

public class Repository
{
    public int Id { get; set; }

    // Full normalized path e.g. /Users/andre/Work/dev/foo
    public string RootPath { get; set; } = default!;

    // Optional short name, e.g. folder name
    public string Name { get; set; } = default!;

    // Navigation
    public virtual ICollection<CodeChunk> Chunks { get; set; } = [];
}
