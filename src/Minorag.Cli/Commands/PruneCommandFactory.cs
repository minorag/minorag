using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Indexing;
using Minorag.Cli.Services;
using Spectre.Console;

namespace Minorag.Cli.Commands;

public static class PruneCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("prune")
        {
            Description = "Remove obsolete, invalid, or orphaned entries from the Minorag index database."
        };

        cmd.Add(CliOptions.DbOption);
        cmd.Add(CliOptions.DryRunOption);
        cmd.Add(CliOptions.PruneOrphanOwnersOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();
            var dryRun = parseResult.GetValue(CliOptions.DryRunOption);
            var pruneOwners = parseResult.GetValue(CliOptions.PruneOrphanOwnersOption);

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var console = scope.ServiceProvider.GetRequiredService<IMinoragConsole>();
            var fs = scope.ServiceProvider.GetRequiredService<IFileSystemHelper>();

            // DB existence check (matches repos / repo-rm behavior)
            if (!fs.FileExists(dbPath))
            {
                console.WriteMarkupLine(
                    $"[red]No index database found at[/] [yellow]{Markup.Escape(dbPath)}[/]. Nothing to prune.");
                return;
            }

            var pruner = scope.ServiceProvider.GetRequiredService<IIndexPruner>();

            AnsiConsole.WriteLine();
            console.WriteMarkupLine("[bold underline]Minorag Prune[/]");
            AnsiConsole.WriteLine();

            var summary = await pruner.PruneAsync(dryRun, pruneOwners, ct);

            if (summary.IndexEmpty)
            {
                console.WriteMarkupLine("[yellow]Index is empty. Nothing to prune.[/]");
                AnsiConsole.WriteLine();
                return;
            }

            var nothingToRemove =
                summary.MissingRepositories == 0 &&
                summary.OrphanedFileRecords == 0 &&
                (!pruneOwners || (summary.OrphanProjects == 0 && summary.OrphanClients == 0));

            if (nothingToRemove)
            {
                console.WriteMarkupLine("[green]No stale or orphaned data found. Index is clean.[/]");
                AnsiConsole.WriteLine();
                return;
            }

            var prefix = dryRun ? "Would prune" : "Pruned";

            if (summary.MissingRepositories > 0)
            {
                console.WriteMarkupLine(
                    $"[green]{prefix}[/] [cyan]{summary.MissingRepositories}[/] missing repositories.");
            }

            if (summary.OrphanedFileRecords > 0)
            {
                console.WriteMarkupLine(
                    $"[green]{prefix}[/] [cyan]{summary.OrphanedFileRecords}[/] orphaned file records (paths with no file on disk).");
            }

            if (pruneOwners)
            {
                if (summary.OrphanProjects > 0)
                {
                    console.WriteMarkupLine(
                        $"[green]{prefix}[/] [cyan]{summary.OrphanProjects}[/] unused projects (no repositories).");
                }

                if (summary.OrphanClients > 0)
                {
                    console.WriteMarkupLine(
                        $"[green]{prefix}[/] [cyan]{summary.OrphanClients}[/] unused clients (no projects).");
                }
            }
            else if (summary.OrphanProjects > 0 || summary.OrphanClients > 0)
            {
                // Informative hint if we *could* prune more with the flag
                console.WriteMarkupLine(
                    "[yellow]ℹ[/] Found unused clients/projects; run with [cyan]--prune-orphan-owners[/] to remove them.");
            }

            AnsiConsole.WriteLine();
        });

        return cmd;
    }
}