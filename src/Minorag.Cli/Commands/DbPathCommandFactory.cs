using System.CommandLine;
using Minorag.Cli.Cli;
using Minorag.Cli.Services;

namespace Minorag.Cli.Commands;

public static class DbPathCommandFactory
{
    public static Command Create()
    {

        var cmd = new Command("db-path")
        {
            Description = "Prints the path to the RAG SQLite database."
        };

        cmd.Add(CliOptions.DbOption);

        cmd.SetAction((parseResult) =>
        {
            var dbFile = parseResult.GetValue(CliOptions.DbOption);

            var finalPath = dbFile != null ? dbFile.FullName : RagEnvironment.GetDefaultDbPath();

            Console.WriteLine(finalPath);
            return Task.CompletedTask;
        });

        return cmd;
    }
}
