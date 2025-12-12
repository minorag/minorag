using System.Runtime.CompilerServices;
using Minorag.Cli.Indexing;
using Minorag.Cli.Models;

namespace Minorag.Cli.Services.Environments;

public class IgnoreRulesValidator(IFileSystemHelper fs, string workingDirectory) : IValidator
{
    private const string Label = ".minoragignore check";
    public async IAsyncEnumerable<EnvironmentCheckResult> ValidateAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var ignorePath = Path.Combine(workingDirectory, ".minoragignore");

        if (!fs.FileExists(ignorePath))
        {
            yield return new EnvironmentCheckResult(
                Label,
                $"No .minoragignore found in current directory ([cyan]{workingDirectory}[/]).",
                EnvironmentIssueSeverity.Warning);

            yield break;
        }

        var lines = await fs.ReadAllLinesAsync(ignorePath, ct);
        var invalid = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.Contains('[') && !line.Contains(']'))
            {
                invalid.Add(line);
            }
        }

        if (invalid.Count == 0)
        {
            yield return new EnvironmentCheckResult(
               Label,
               $".minoragignore parsed successfully ([cyan]{lines.Length}[/] rules checked)",
               EnvironmentIssueSeverity.Success);

            yield break;
        }

        yield return new EnvironmentCheckResult(
                       Label,
                       $".minoragignore contains [cyan]{invalid.Count}[/] invalid rules:",
                       EnvironmentIssueSeverity.Success);

        foreach (var rule in invalid)
        {
            yield return new EnvironmentCheckResult(
                      Label,
                      $"Invalid ignore rule [red]{rule}[/], ignoring this pattern.",
                      EnvironmentIssueSeverity.Warning);
        }
    }
}
