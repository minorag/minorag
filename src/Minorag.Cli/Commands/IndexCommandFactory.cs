using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Indexing;
using Minorag.Core.Models.Domain;
using Minorag.Core.Services;
using Minorag.Core.Store;

namespace Minorag.Cli.Commands;

public static class IndexCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("index")
        {
            Description = "Index a repository into the RAG store"
        };

        cmd.Add(CliOptions.RepoOption);
        cmd.Add(CliOptions.DbOption);
        cmd.Add(CliOptions.ClientOption);
        cmd.Add(CliOptions.ProjectOption);
        cmd.Add(CliOptions.ReindexOption);
        cmd.Add(CliOptions.ExcludeOption);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var repoFromArg = parseResult.GetValue(CliOptions.RepoOption);
            var repoRoot = repoFromArg ?? RagEnvironment.GetRepoRootOrCurrent();

            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            var clientName = parseResult.GetValue(CliOptions.ClientOption);
            var projectName = parseResult.GetValue(CliOptions.ProjectOption);
            var reindex = parseResult.GetValue(CliOptions.ReindexOption);
            var excludePatterns = parseResult.GetValue(CliOptions.ExcludeOption) ?? [];

            Console.WriteLine($"Indexing '{repoRoot.FullName}' → '{dbPath}'");

            using var host = await HostFactory.BuildHost(dbPath, cancellationToken);
            using var scope = host.Services.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();
            var scopeService = scope.ServiceProvider.GetRequiredService<IIndexScopeService>();
            var indexer = scope.ServiceProvider.GetRequiredService<IIndexer>();

            Repository repo;
            try
            {
                repo = await scopeService.EnsureClientProjectRepoAsync(
                    repoRoot,
                    clientName,
                    projectName,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await indexer.IndexAsync(
                repo.RootPath!,
                reindex,
                excludePatterns,
                cancellationToken);
        });

        return cmd;
    }
}