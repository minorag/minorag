using Microsoft.Extensions.Configuration;
using Minorag.Cli.Indexing;
using Minorag.Cli.Models;
using Spectre.Console;

namespace Minorag.Cli.Services.Environments;

public interface IEnvironmentDoctor
{
    Task RunAsync(string dbPath, string workingDirectory, CancellationToken ct);
}

public sealed class EnvironmentDoctor(
    DatabaseValidatorFactory databaseValidatorFactory,
    IgnoreRulesValidatorFactory ignoreRulesValidatorFactory,
    OllamaValidator ollamaValidator,
    ConfigValidatorFactory configValidatorFactory,
    IConfiguration configuration,
    IIndexPruner indexPruner,
    IMinoragConsole console) : IEnvironmentDoctor
{
    public async Task RunAsync(string dbPath, string workingDirectory, CancellationToken ct)
    {
        var configuredDbPath = configuration.GetSection("Database")["Path"];

        await RunDatabaseChecksAsync(dbPath, ct);

        await RunIndexHealthChecksAsync(ct);
        await RunIgnoreRulesChecks(workingDirectory, ct);
        await RunOllamaChecksAsync(ct);
        await RunConfigChecks(dbPath, configuredDbPath, ct);
    }

    private void Print(EnvironmentCheckResult result)
    {
        switch (result.Severity)
        {
            case EnvironmentIssueSeverity.Error:
                {
                    console.WriteMarkupLine($"[red]✖[/] [bold] {result.Label} [/] {result.Description}");
                    break;
                }
            case EnvironmentIssueSeverity.Success:
                {
                    console.WriteMarkupLine($"[green]✔[/] [bold] {result.Label} [/] {result.Description}");
                    break;
                }
            case EnvironmentIssueSeverity.Warning:
                {
                    console.WriteMarkupLine($"[yellow]⚠[/] [bold] {result.Label} [/] {result.Description}");
                    break;
                }
            case EnvironmentIssueSeverity.Info:
                {
                    console.WriteMarkupLine($"[bold] {result.Label} [/] {result.Description}");
                    break;
                }
        }

        if (!string.IsNullOrWhiteSpace(result.Hint))
        {
            console.WriteMarkupLine(result.Hint);
        }
    }

    private async Task RunDatabaseChecksAsync(string dbPath, CancellationToken ct)
    {
        var validator = databaseValidatorFactory.Create(dbPath);
        var results = validator.ValidateAsync(ct);
        await PrintResults(results, "[bold]Database & Schema[/]", ct);
    }

    private async Task PrintResults(IAsyncEnumerable<EnvironmentCheckResult> results, string label, CancellationToken ct)
    {
        console.WriteMarkupLine(label);

        await foreach (var result in results.WithCancellation(ct))
        {
            Print(result);
        }

        console.WriteLine();
    }

    private async Task RunIndexHealthChecksAsync(CancellationToken ct)
    {
        console.WriteMarkupLine("[bold]Indexed Repositories Health[/]");

        try
        {
            var summary = await indexPruner.PruneAsync(
                dryRun: true,
                pruneOrphanOwners: false,
                ct);

            if (summary.IndexEmpty)
            {
                console.WriteMarkupLine("[yellow]No repositories indexed yet. Index is empty.[/]");
                console.WriteLine();
                return;
            }

            console.WriteMarkupLine(
                "[green]✔[/] {0} repositories, {1} chunks, {2} clients, {3} projects in index",
                summary.TotalRepositories,
                summary.TotalChunks,
                summary.TotalClients,
                summary.TotalProjects);

            if (summary.OrphanChunks == 0)
            {
                console.WriteMarkupLine("[green]✔[/] No orphaned chunks");
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] There are chunks referenced to deleted repository. Run [cyan]`minorag prune`[/] to clean.",
                    summary.MissingRepositories);
            }

            if (summary.MissingRepositories == 0)
            {
                console.WriteMarkupLine("[green]✔[/] All repository directories still exist on disk");
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] {0} repositories no longer exist on disk. Run [cyan]`minorag prune`[/] to clean.",
                    summary.MissingRepositories);
            }

            if (summary.OrphanedFileRecords == 0)
            {
                console.WriteMarkupLine("[green]✔[/] All indexed files still exist on disk");
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠ {0} indexed files no longer exist on disk. Run [cyan]`minorag prune`[/] or re-index affected repos.[/]",
                    summary.OrphanedFileRecords);

                if (summary.MissingFileSamples is { Count: > 0 })
                {
                    var toShow = summary.MissingFileSamples.ToList();

                    foreach (var sample in toShow)
                    {
                        console.WriteMarkupLine(
                            "    [yellow]⚠[/] Missing file [cyan]{0}[/] (repo root [blue]{1}[/])",
                            Markup.Escape(sample.RelativePath),
                            Markup.Escape(sample.RepositoryRoot));
                    }

                    var remaining = summary.OrphanedFileRecords - toShow.Count;
                    if (remaining > 0)
                    {
                        console.WriteMarkupLine(
                            "    [grey]… and {0} more. Run [cyan]`minorag prune`[/] to refresh the index.[/]",
                            remaining);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            console.WriteMarkupLine(
                "[red]✖[/] Error while checking index health: [yellow]{0}[/]",
                Markup.Escape(ex.Message));
        }

        console.WriteLine();
    }

    private async Task RunIgnoreRulesChecks(string workingDirectory, CancellationToken ct)
    {
        var validator = ignoreRulesValidatorFactory.Create(workingDirectory);
        var results = validator.ValidateAsync(ct);

        await PrintResults(results, "[bold]Ignore Rules (.minoragignore)[/]", ct);
    }

    private async Task RunOllamaChecksAsync(CancellationToken ct)
    {
        var results = ollamaValidator.ValidateAsync(ct);
        await PrintResults(results, "[bold]Ollama Environment[/]", ct);
    }

    private async Task RunConfigChecks(string dbPath, string? configuredDbPath, CancellationToken ct)
    {
        var validator = configValidatorFactory.Create(dbPath, configuredDbPath);
        var results = validator.ValidateAsync(ct);
        await PrintResults(results, "[bold]Minorag Configuration[/]", ct);
    }
}