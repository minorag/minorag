using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Services;
using Minorag.Cli.Store;

namespace Minorag.Cli.Commands;

public static class PromptCommandFactory
{
    public static Command Create()
    {

        var cmd = new Command("prompt")
        {
            Description = "Generate a ChatGPT/LLM-ready markdown prompt with indexed code context"
        };

        cmd.Add(CliOptions.QuestionArgument);
        cmd.Add(CliOptions.DbOption);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var question = parseResult.GetRequiredValue(CliOptions.QuestionArgument);
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();

            var searcher = scope.ServiceProvider.GetRequiredService<ISearcher>();
            var formatter = scope.ServiceProvider.GetRequiredService<IPromptFormatter>();

            // Retrieval only (no LLM)
            var context = await searcher.RetrieveAsync(question, true, topK: 7, ct: cancellationToken);

            var markdown = formatter.Format(context, question);

            // Pure markdown to stdout → user can paste into ChatGPT / other LLMs
            Console.WriteLine(markdown);
        });

        return cmd;
    }
}
