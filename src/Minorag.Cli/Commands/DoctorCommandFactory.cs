using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Services;
using Spectre.Console;

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

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var doctor = scope.ServiceProvider.GetRequiredService<IEnvironmentDoctor>();

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold underline]Minorag Doctor[/]");
            AnsiConsole.WriteLine();

            await doctor.RunAsync(dbPath, repoRoot.FullName, ct);

            AnsiConsole.WriteLine();
        });

        return cmd;
    }
}