using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Minorag.Core.Indexing;
using Minorag.Core.Models;

namespace Minorag.Core.Services.Environments;

public interface IEnvironmentDoctor
{
    IAsyncEnumerable<EnvironmentCheckResult> DiagnoseAsync(
        string dbPath,
        string workingDirectory,
        CancellationToken ct);
}

public sealed class EnvironmentDoctor(
    DatabaseValidatorFactory databaseValidatorFactory,
    IgnoreRulesValidatorFactory ignoreRulesValidatorFactory,
    OllamaValidator ollamaValidator,
    ConfigValidatorFactory configValidatorFactory,
    IConfiguration configuration,
    IIndexPruner indexPruner) : IEnvironmentDoctor
{
    public async IAsyncEnumerable<EnvironmentCheckResult> DiagnoseAsync(
        string dbPath,
        string workingDirectory,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var configuredDbPath = configuration.GetSection("Database")["Path"];

        // Database & Schema
        yield return Section("Database & Schema");
        await foreach (var r in databaseValidatorFactory.Create(dbPath).ValidateAsync(ct).WithCancellation(ct))
            yield return r;
        yield return BlankLine();

        // Index health (special logic)
        await foreach (var r in IndexHealthAsync(ct).WithCancellation(ct))
            yield return r;

        // Ignore rules
        yield return Section("Ignore Rules (.minoragignore)");
        await foreach (var r in ignoreRulesValidatorFactory.Create(workingDirectory).ValidateAsync(ct).WithCancellation(ct))
            yield return r;
        yield return BlankLine();

        // Ollama
        yield return Section("Ollama Environment");
        await foreach (var r in ollamaValidator.ValidateAsync(ct).WithCancellation(ct))
            yield return r;
        yield return BlankLine();

        // Config
        yield return Section("Minorag Configuration");
        await foreach (var r in configValidatorFactory.Create(dbPath, configuredDbPath).ValidateAsync(ct).WithCancellation(ct))
            yield return r;
        yield return BlankLine();
    }

    private async IAsyncEnumerable<EnvironmentCheckResult> IndexHealthAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return Section("Indexed Repositories Health");

        // IMPORTANT: no yield inside try/catch. We only do the await here.
        object? summaryObj = null;
        Exception? error = null;

        try
        {
            // Keep your original call
            summaryObj = await indexPruner.PruneAsync(
                dryRun: true,
                pruneOrphanOwners: false,
                ct);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (error != null)
        {
            yield return new EnvironmentCheckResult(
                "Index health",
                $"Error while checking index health: {error.Message}",
                EnvironmentIssueSeverity.Error);

            yield return BlankLine();
            yield break;
        }

        // We don't know your summary type name from this snippet, so we treat it dynamically.
        // This compiles as long as PruneAsync returns a concrete type with these properties.
        dynamic summary = summaryObj!;

        if ((bool)summary.IndexEmpty)
        {
            yield return new EnvironmentCheckResult(
                "Index",
                "No repositories indexed yet. Index is empty.",
                EnvironmentIssueSeverity.Warning);

            yield return BlankLine();
            yield break;
        }

        yield return new EnvironmentCheckResult(
            "Index",
            $"{summary.TotalRepositories} repositories, {summary.TotalChunks} chunks, {summary.TotalClients} clients, {summary.TotalProjects} projects in index",
            EnvironmentIssueSeverity.Success);

        if ((int)summary.OrphanChunks == 0)
        {
            yield return new EnvironmentCheckResult("Orphaned chunks", "No orphaned chunks", EnvironmentIssueSeverity.Success);
        }
        else
        {
            var r = new EnvironmentCheckResult(
                "Orphaned chunks",
                "There are chunks referenced to deleted repository.",
                EnvironmentIssueSeverity.Warning);
            r.Hint = "Run [cyan]`minorag prune`[/] to clean.";
            yield return r;
        }

        if ((int)summary.MissingRepositories == 0)
        {
            yield return new EnvironmentCheckResult("Repository roots", "All repository directories still exist on disk", EnvironmentIssueSeverity.Success);
        }
        else
        {
            var r = new EnvironmentCheckResult(
                "Repository roots",
                $"{summary.MissingRepositories} repositories no longer exist on disk.",
                EnvironmentIssueSeverity.Warning);
            r.Hint = "Run [cyan]`minorag prune`[/] to clean.";
            yield return r;
        }

        if ((int)summary.OrphanedFileRecords == 0)
        {
            yield return new EnvironmentCheckResult("Indexed files", "All indexed files still exist on disk", EnvironmentIssueSeverity.Success);
        }
        else
        {
            var r = new EnvironmentCheckResult(
                "Indexed files",
                $"{summary.OrphanedFileRecords} indexed files no longer exist on disk. Run `minorag prune` or re-index affected repos.",
                EnvironmentIssueSeverity.Warning);
            r.Hint = "Run [cyan]`minorag prune`[/] or re-index affected repos.";
            yield return r;

            // Missing file samples
            if (summary.MissingFileSamples != null && summary.MissingFileSamples.Count > 0)
            {
                var toShow = ((IEnumerable<dynamic>)summary.MissingFileSamples).ToList();
                foreach (var sample in toShow)
                {
                    yield return new EnvironmentCheckResult(
                        "Missing file",
                        $"Missing file {sample.RelativePath} (repo root {sample.RepositoryRoot})",
                        EnvironmentIssueSeverity.Warning);
                }

                var remaining = (int)summary.OrphanedFileRecords - toShow.Count;
                if (remaining > 0)
                {
                    var more = new EnvironmentCheckResult(
                        "Missing file",
                        $"… and {remaining} more.",
                        EnvironmentIssueSeverity.Info);
                    more.Hint = "Run [cyan]`minorag prune`[/] to refresh the index.";
                    yield return more;
                }
            }
        }

        yield return BlankLine();
    }

    private static EnvironmentCheckResult Section(string title)
        => new(title, "", EnvironmentIssueSeverity.Info);

    private static EnvironmentCheckResult BlankLine()
        => new("", "", EnvironmentIssueSeverity.Info);
}