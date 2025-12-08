using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Services;
using Minorag.Cli.Store;
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

            // 1. DB existence check
            if (!File.Exists(dbPath))
            {
                AnsiConsole.MarkupLine(
                    $"[red]No index database found at[/] [yellow]{Markup.Escape(dbPath)}[/]. " +
                    "Run [cyan]`minorag index`[/] first.");
                return;
            }

            var repos = await GetRepositories(dbPath, ct);

            if (repos.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No repositories indexed yet.[/]");
                return;
            }

            RenderTable(repos);
        });

        return cmd;
    }

    private static async Task<Repository[]> GetRepositories(string dbPath, CancellationToken ct)
    {
        using var host = HostFactory.BuildHost(dbPath);
        using var scope = host.Services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();

        var repos = await dbContext.Repositories
            .Include(r => r.Project)
            .ThenInclude(p => p.Client)
            .ToListAsync(ct);

        var ordered = repos
            .OrderBy(r => r.Project?.Client?.Name ?? string.Empty)
            .ThenBy(r => r.Project?.Name ?? string.Empty)
            .ThenBy(r => r.Name)
            .ToArray();

        return ordered;
    }

    private static void RenderTable(Repository[] ordered)
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
            var clientName = repo.Project?.Client?.Name ?? "-";
            var projectName = repo.Project?.Name ?? "-";
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