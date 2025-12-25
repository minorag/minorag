#nullable disable

using System.ComponentModel.DataAnnotations;

namespace Minorag.Core.Models.Domain;

public class Client
{
    [Key]
    public int Id { get; init; }
    public string Name { get; init; }
    public string Slug { get; init; }

    public virtual IList<Project> Projects { get; set; }
}