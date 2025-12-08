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

        cmd.Add(CliOptions.TopKOption);
        cmd.Add(CliOptions.QuestionArgument);
        cmd.Add(CliOptions.RepoNameOption);
        cmd.Add(CliOptions.RepoNamesCsvOption);
        cmd.Add(CliOptions.DbOption);
        cmd.Add(CliOptions.VerboseOption);
        cmd.Add(CliOptions.NoLlmOption);
        cmd.Add(CliOptions.AllReposOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var question = parseResult.GetRequiredValue(CliOptions.QuestionArgument);
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            var verbose = parseResult.GetValue(CliOptions.VerboseOption);
            var noLlm = parseResult.GetValue(CliOptions.NoLlmOption);

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var searcher = scope.ServiceProvider.GetRequiredService<ISearcher>();
            var presenter = scope.ServiceProvider.GetRequiredService<IConsoleSearchPresenter>();
            var ragOptions = scope.ServiceProvider.GetRequiredService<IOptions<RagOptions>>();

            var scopeResolver = scope.ServiceProvider.GetRequiredService<ScopeResolver>();

            var explicitRepoNames = parseResult.GetValue(CliOptions.RepoNameOption) ?? [];
            var reposCsv = parseResult.GetValue(CliOptions.RepoNamesCsvOption);
            var projectName = parseResult.GetValue(CliOptions.ProjectOption);
            var clientName = parseResult.GetValue(CliOptions.ClientOption);
            var allRepos = parseResult.GetValue(CliOptions.AllReposOption);

            var repositories = await scopeResolver.ResolveScopeAsync(
                  Environment.CurrentDirectory,
                  repoNames: explicitRepoNames,
                  reposCsv: reposCsv,
                  projectName,
                  clientName,
                  allRepos,
                  ct);
            var repoIds = repositories.Select(r => r.Id).ToList();

            var topKOverride = parseResult.GetValue(CliOptions.TopKOption);
            var effectiveTopK = topKOverride ?? ragOptions.Value.TopK;

            var context = await searcher.RetrieveAsync(
                question,
                verbose,
                repositoryIds: repoIds,
                topK: effectiveTopK,
                ct: ct);

            presenter.PresentRetrieval(context, verbose);

            if (!noLlm && context.HasResults)
            {
                var result = await searcher.AnswerAsync(context, useLlm: true, ct: ct);
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