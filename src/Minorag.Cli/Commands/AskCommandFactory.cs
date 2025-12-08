using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Models;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Services;
using Minorag.Cli.Store;

namespace Minorag.Cli.Commands;

public static class AskCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("ask")
        {
            Description = "Query the indexed codebase"
        };

        cmd.Add(CliOptions.QuestionArgument);
        cmd.Add(CliOptions.DbOption);
        cmd.Add(CliOptions.VerboseOption);
        cmd.Add(CliOptions.NoLlmOption);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var question = parseResult.GetRequiredValue(CliOptions.QuestionArgument);
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            var verbose = parseResult.GetValue(CliOptions.VerboseOption);
            var noLlm = parseResult.GetValue(CliOptions.NoLlmOption);

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();

            var searcher = scope.ServiceProvider.GetRequiredService<ISearcher>();
            var presenter = scope.ServiceProvider.GetRequiredService<IConsoleSearchPresenter>();
            var ragOptions = scope.ServiceProvider.GetRequiredService<IOptions<RagOptions>>();

            var topKOverride = parseResult.GetValue(CliOptions.TopKOption);
            var effectiveTopK = topKOverride ?? ragOptions.Value.TopK;

            var context = await searcher.RetrieveAsync(question, verbose, topK: effectiveTopK, ct: cancellationToken);

            presenter.PresentRetrieval(context, verbose);

            if (!noLlm && context.HasResults)
            {
                var result = await searcher.AnswerAsync(context, useLlm: true, ct: cancellationToken);
                presenter.PresentAnswer(result, showLlm: true);
            }
            else if (noLlm)
            {
                presenter.PresentAnswer(new SearchResult(context.Question, context.Chunks, null), showLlm: false);
            }
        });

        return cmd;
    }
}