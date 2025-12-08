using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Models.Options;
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
        cmd.Add(CliOptions.ClientOption);
        cmd.Add(CliOptions.ProjectOption);
        cmd.Add(CliOptions.DbOption);
        cmd.Add(CliOptions.TopKOption);
        cmd.Add(CliOptions.RepoNameOption);
        cmd.Add(CliOptions.RepoNamesCsvOption);
        cmd.Add(CliOptions.AllReposOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var question = parseResult.GetRequiredValue(CliOptions.QuestionArgument);
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();

            var searcher = scope.ServiceProvider.GetRequiredService<ISearcher>();
            var formatter = scope.ServiceProvider.GetRequiredService<IPromptFormatter>();

            var ragOptions = scope.ServiceProvider.GetRequiredService<IOptions<RagOptions>>();

            var topKOverride = parseResult.GetValue(CliOptions.TopKOption);
            var effectiveTopK = topKOverride ?? ragOptions.Value.TopK;

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

            // Retrieval only (no LLM)
            var context = await searcher.RetrieveAsync(
                question,
                verbose: true,
                topK: effectiveTopK,
                repositoryIds: repoIds,
                ct: ct);

            var markdown = formatter.Format(context, question);

            // Pure markdown to stdout → user can paste into ChatGPT / other LLMs
            Console.WriteLine(markdown);
        });

        return cmd;
    }
}
