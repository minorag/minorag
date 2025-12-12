using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Indexing;
using Minorag.Cli.Services;
using Minorag.Cli.Store;
using Spectre.Console;

namespace Minorag.Cli.Commands;

/// <summary>
/// Implements the `minorag status` read‑only command.
/// </summary>
public static class StatusCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("status")
        {
            Description = "Show health & contents of the Minorag index database."
        };

        cmd.Add(CliOptions.DbOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            // No database file → friendly message.
            if (!File.Exists(dbPath))
            {
                AnsiConsole.MarkupLine(
                    $"[red]No index database found at[/] [yellow]{Markup.Escape(dbPath)}[/]. " +
                    "Run [cyan]`minorag index`[/] first.");
                return;
            }

            // Build host & get DbContext.
            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<RagDbContext>();
            var pruner = scope.ServiceProvider.GetRequiredService<IIndexPruner>();

            var status = await pruner.CalculateStatus(ct);

            // ---- Last indexed (top 5) ------------------------------------------
            var lastIndexed = await db.Repositories
                                      .AsNoTracking()
                                      .OrderByDescending(r => r.LastIndexedAt)
                                      .Take(5)
                                      .Select(r => new
                                      {
                                          r.Name,
                                          r.LastIndexedAt
                                      })
                                      .ToListAsync(ct);

            // ---- Output ---------------------------------------------------------
            var dbTable = new Table()
                .AddColumn("Metric")
                .AddColumn("Value");

            dbTable.AddRow("[cyan]Database[/]", dbPath);
            dbTable.AddRow("[cyan]Clients[/]", status.TotalClients.ToString("N0"));
            dbTable.AddRow("[cyan]Projects[/]", status.TotalProjects.ToString("N0"));
            dbTable.AddRow("[cyan]Repositories[/]", status.TotalRepos.ToString("N0"));
            dbTable.AddRow("[cyan]Files indexed[/]", status.TotalFiles.ToString("N0"));
            dbTable.AddRow("[cyan]Chunks[/]", status.TotalChunks.ToString("N0"));

            AnsiConsole.Write(dbTable);

            // ----- Last Indexed section ----------------------------------------
            AnsiConsole.WriteLine();
            if (lastIndexed.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No indexing activity recorded yet.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[bold underline]Last Indexed (top 5):[/]");

                foreach (var item in lastIndexed)
                {
                    var ts = item.LastIndexedAt?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
                    AnsiConsole.MarkupLine($"- [green]{Markup.Escape(item.Name)}[/] @ [yellow]{ts}[/]");
                }
            }
        });

        return cmd;
    }
}