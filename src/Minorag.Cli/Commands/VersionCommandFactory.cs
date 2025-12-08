using System.CommandLine;
using System.Reflection;

namespace Minorag.Cli.Commands;

public static class VersionCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("version")
        {
            Description = "Show Minorag CLI version"
        };

        cmd.SetAction((parseResult, ct) =>
        {
            var assembly = typeof(VersionCommandFactory).Assembly;

            var infoVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            var fallback = assembly.GetName().Version?.ToString();
            var version = infoVersion ?? fallback ?? "unknown";

            Console.WriteLine($"minorag {version}");

            return Task.CompletedTask;
        });

        return cmd;
    }
}