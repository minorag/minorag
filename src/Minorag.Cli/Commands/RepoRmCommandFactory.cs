using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Services;
using Minorag.Cli.Store;
using Spectre.Console;

namespace Minorag.Cli.Commands;

public static class RepoRmCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("repo-rm")
        {
            Description = "Remove a specific repository (and its files/chunks) from the Minorag index."
        };

        cmd.Add(CliOptions.RepoOption);
        cmd.Add(CliOptions.DbOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var repoDir = parseResult.GetValue(CliOptions.RepoOption);
            var dbFile = parseResult.GetValue(CliOptions.DbOption);

            var repoRoot = repoDir ?? RagEnvironment.GetRepoRootOrCurrent();
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            if (!File.Exists(dbPath))
            {
                AnsiConsole.MarkupLine(
                    $"[red]No index database found at[/] [yellow]{Markup.Escape(dbPath)}[/]. " +
                    "Run [cyan]`minorag index`[/] first.");
                return;
            }

            var normalizedRoot = NormalizePath(repoRoot.FullName);

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var store = scope.ServiceProvider.GetRequiredService<ISqliteStore>();

            var repo = await store.GetRepositoryAsync(normalizedRoot, ct);

            if (repo is null)
            {
                AnsiConsole.MarkupLine("[yellow]Repository not found in index.[/]");
                return;
            }

            await store.RemoveRepository(repo.Id, ct);

            AnsiConsole.MarkupLine("[green]Removed repository:[/]");
            AnsiConsole.MarkupLine(
                $"  Name: [cyan]{Markup.Escape(repo.Name ?? "(unnamed)")}" +
                $"[/]{Environment.NewLine}" +
                $"  Path: [grey]{Markup.Escape(repo.RootPath)}[/]");
        });

        return cmd;
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}