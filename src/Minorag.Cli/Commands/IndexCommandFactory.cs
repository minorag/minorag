using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Indexing;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Services;
using Minorag.Cli.Store;

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

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var repoFromArg = parseResult.GetValue(CliOptions.RepoOption);
            var repoRoot = repoFromArg ?? RagEnvironment.GetRepoRootOrCurrent();

            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            var clientName = parseResult.GetValue(CliOptions.ClientOption);
            var projectName = parseResult.GetValue(CliOptions.ProjectOption);

            Console.WriteLine($"Indexing '{repoRoot.FullName}' → '{dbPath}'");

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();
            var scopeService = scope.ServiceProvider.GetRequiredService<IIndexScopeService>();

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
                Console.WriteLine("Aborting indexing. Repository mapping was not changed.");
                return;
            }

            var indexer = scope.ServiceProvider.GetRequiredService<IIndexer>();
            await indexer.IndexAsync(repoRoot.FullName, cancellationToken);

            Console.WriteLine("Indexing completed.");
        });

        return cmd;
    }
}