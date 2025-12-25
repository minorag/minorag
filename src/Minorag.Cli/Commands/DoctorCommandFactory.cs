using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Core.Models;
using Minorag.Core.Services;
using Minorag.Core.Services.Environments;

namespace Minorag.Cli.Commands;

public static class DoctorCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("doctor")
        {
            Description = "Analyze your Minorag environment and report potential problems."
        };

        cmd.Add(CliOptions.DbOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            var repoRoot = RagEnvironment.GetRepoRootOrCurrent();

            using var host = await HostFactory.BuildHost(dbPath, ct);
            using var scope = host.Services.CreateScope();
            var console = scope.ServiceProvider.GetRequiredService<IMinoragConsole>();
            var doctor = scope.ServiceProvider.GetRequiredService<IEnvironmentDoctor>();

            console.WriteLine();
            console.WriteMarkupLine("[bold underline]Minorag Doctor[/]");
            console.WriteLine();

            await foreach (var result in doctor.DiagnoseAsync(dbPath, repoRoot.FullName, ct).WithCancellation(ct))
            {
                Print(console, result);
            }

            console.WriteLine();
        });

        return cmd;
    }

    private static void Print(IMinoragConsole console, EnvironmentCheckResult result)
    {
        // Blank line marker
        if (string.IsNullOrWhiteSpace(result.Label) && string.IsNullOrWhiteSpace(result.Description))
        {
            console.WriteLine();
            return;
        }

        // Section header marker (Info + empty description)
        if (result.Severity == EnvironmentIssueSeverity.Info && string.IsNullOrWhiteSpace(result.Description))
        {
            console.WriteMarkupLine($"[bold]{result.Label}[/]");
            return;
        }

        switch (result.Severity)
        {
            case EnvironmentIssueSeverity.Error:
                console.WriteMarkupLine($"[red]✖[/] [bold]{result.Label}[/] {result.Description}");
                break;
            case EnvironmentIssueSeverity.Success:
                console.WriteMarkupLine($"[green]✔[/] [bold]{result.Label}[/] {result.Description}");
                break;
            case EnvironmentIssueSeverity.Warning:
                console.WriteMarkupLine($"[yellow]⚠[/] [bold]{result.Label}[/] {result.Description}");
                break;
            case EnvironmentIssueSeverity.Info:
                console.WriteMarkupLine($"[bold]{result.Label}[/] {result.Description}");
                break;
        }

        if (!string.IsNullOrWhiteSpace(result.Hint))
            console.WriteMarkupLine(result.Hint);
    }
}