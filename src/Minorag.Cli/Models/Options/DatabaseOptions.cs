namespace Minorag.Cli.Models.Options;

public sealed record DatabaseOptions
{
    /// <summary>
    /// Optional fixed path. If null, MinoragEnvironment decides (e.g. ~/.minorag/index.db).
    /// </summary>
    public string? Path { get; init; }
}