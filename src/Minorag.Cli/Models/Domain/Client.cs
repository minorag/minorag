#nullable disable

namespace Minorag.Cli.Models.Domain;

public class Client
{
    public int Id { get; init; }
    public string Name { get; init; }
    public string Slug { get; init; }

    public virtual IList<Project> Projects { get; set; }
}