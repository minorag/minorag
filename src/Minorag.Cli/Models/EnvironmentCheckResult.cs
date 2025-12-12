namespace Minorag.Cli.Models;

public record EnvironmentCheckResult(
    string Label,
    string Description,
    EnvironmentIssueSeverity Severity)
{
    public string? Hint { get; set; }
}
