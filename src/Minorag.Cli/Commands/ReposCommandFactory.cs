using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Core.Models.ViewModels;
using Minorag.Core.Services;
using Minorag.Core.Store;
using Spectre.Console;

namespace Minorag.Cli.Commands;

public static class ReposCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("repos")
        {
            Description = "List all repositories indexed in the Minorag RAG store."
        };

        cmd.Add(CliOptions.DbOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();
            var console = scope.ServiceProvider.GetRequiredService<IMinoragConsole>();
            var fs = scope.ServiceProvider.GetRequiredService<IFileSystemHelper>();
            var store = scope.ServiceProvider.GetRequiredService<ISqliteStore>();

            // 1. DB existence check
            if (!fs.FileExists(dbPath))
            {
                console.WriteMarkupLine(
                    $"[red]No index database found at[/] [yellow]{Markup.Escape(dbPath)}[/]. " +
                    "Run [cyan]`minorag index`[/] first.");
                return;
            }

            var repos = await store.GetRepositories(ct);

            if (repos.Length == 0)
            {
                console.WriteMarkupLine("[yellow]No repositories indexed yet.[/]");
                return;
            }

            RenderTable(repos);
        });

        return cmd;
    }



    private static void RenderTable(RepositoryVm[] ordered)
    {
        var table = new Table
        {
            Border = TableBorder.Rounded
        };

        table.AddColumn("CLIENT");
        table.AddColumn("PROJECT");
        table.AddColumn("REPOSITORY");
        table.AddColumn("PATH");
        table.AddColumn("LAST INDEXED");

        foreach (var repo in ordered)
        {
            var clientName = repo.ClientName ?? "-";
            var projectName = repo.ProjectName ?? "-";
            var repoName = repo.Name;
            var path = repo.RootPath;
            var lastIndexedAt = repo.LastIndexedAt is not null ? $"{repo.LastIndexedAt} UTC" : "";

            table.AddRow(
                clientName,
                projectName,
                repoName,
                Markup.Escape(path),
                lastIndexedAt
            );
        }

        AnsiConsole.Write(table);
    }
}