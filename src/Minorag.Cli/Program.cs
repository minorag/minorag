using System.CommandLine;
using Minorag.Cli.Configuration;
using Minorag.Cli.Indexing;
using Minorag.Cli.Services;
using Minorag.Cli.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minorag.Cli.Models;
using Microsoft.Extensions.Configuration;

// -----------------------------------------------------------------------------
// Arguments & Options
// -----------------------------------------------------------------------------

var repoArg = new Option<DirectoryInfo>("--repo")
{
    Description = "Path to the repository to index (default: current git root)"
};

var questionArg = new Argument<string>("question")
{
    Description = "Question to ask about the indexed codebase"
};

var dbOption = new Option<FileInfo?>("--db")
{
    Description = "Path to SQLite database file (default: ~/.minorag/index.db)"
};
dbOption.Aliases.Add("-d");

var verboseOption = new Option<bool>("--verbose")
{
    Description = "Print retrieved snippets (context) before the LLM answer"
};
verboseOption.Aliases.Add("-v");

var noLlmOption = new Option<bool>("--no-llm")
{
    Description = "Only show retrieved files/snippets without asking the LLM"
};

// -----------------------------------------------------------------------------
// index
// -----------------------------------------------------------------------------

var indexCommand = new Command("index")
{
    Description = "Index a repository into the RAG store"
};

indexCommand.Add(repoArg);
indexCommand.Add(dbOption);

indexCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var repoFromArg = parseResult.GetValue(repoArg);
    var repoRoot = repoFromArg ?? RagEnvironment.GetRepoRootOrCurrent();

    var dbFile = parseResult.GetValue(dbOption);
    var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

    Console.WriteLine($"Indexing '{repoRoot.FullName}' → '{dbPath}'");

    using var host = BuildHost(dbPath);
    using var scope = host.Services.CreateScope();

    var dbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();
    await dbContext.Database.EnsureCreatedAsync(cancellationToken);

    var indexer = scope.ServiceProvider.GetRequiredService<IIndexer>();
    await indexer.IndexAsync(repoRoot.FullName, cancellationToken);

    Console.WriteLine("Indexing completed.");
});

// -----------------------------------------------------------------------------
// ask
// -----------------------------------------------------------------------------

var askCommand = new Command("ask")
{
    Description = "Query the indexed codebase"
};

askCommand.Add(questionArg);
askCommand.Add(dbOption);
askCommand.Add(verboseOption);
askCommand.Add(noLlmOption);

askCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var question = parseResult.GetRequiredValue(questionArg);
    var dbFile = parseResult.GetValue(dbOption);
    var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

    var verbose = parseResult.GetValue(verboseOption);
    var noLlm = parseResult.GetValue(noLlmOption);

    using var host = BuildHost(dbPath);
    using var scope = host.Services.CreateScope();

    var dbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();
    await dbContext.Database.EnsureCreatedAsync(cancellationToken);

    var searcher = scope.ServiceProvider.GetRequiredService<ISearcher>();
    var presenter = scope.ServiceProvider.GetRequiredService<IConsoleSearchPresenter>();

    // 1. Retrieval
    var context = await searcher.RetrieveAsync(question, verbose, topK: 7, ct: cancellationToken);
    presenter.PresentRetrieval(context, verbose);

    // 2. LLM (optional)
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

var root = new RootCommand("Minorag CLI")
{
    Description = """
                  Command-line utilities for indexing and querying codebases using RAG.

                  Typical usage:
                    # Index multiple repos into one global DB
                    cd ~/dev/backend  && minorag index
                    cd ~/dev/frontend && minorag index

                    # Ask questions across all indexed repos
                    minorag ask "where is the code responsible for fetching users?"

                  Options:
                    --db, -d      Override default SQLite DB (default: ~/.minorag/index.db)
                    --verbose,-v  Print retrieved snippets
                    --no-llm      Only show retrieved files/snippets, no LLM call
                  """
};

var dbPathCommand = new Command("db-path")
{
    Description = "Prints the path to the RAG SQLite database."
};

dbPathCommand.Add(dbOption);

dbPathCommand.SetAction((parseResult) =>
{
    var dbFile = parseResult.GetValue(dbOption);

    var finalPath = dbFile != null ? dbFile.FullName : RagEnvironment.GetDefaultDbPath();

    Console.WriteLine(finalPath);
    return Task.CompletedTask;
});

root.Add(dbPathCommand);
root.Add(indexCommand);
root.Add(askCommand);

return await root.Parse(args).InvokeAsync();

static IHost BuildHost(string dbPath)
{
    var appBase = AppContext.BaseDirectory;
    var settings = new HostApplicationBuilderSettings
    {
        ContentRootPath = appBase,
        ApplicationName = "Minorag.Cli"
    };

    var builder = Host.CreateApplicationBuilder(settings);

    builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables(prefix: "MINORAG_");

    builder.Services.SetupConfiguration(builder.Configuration);
    builder.ConfigureLogging();
    builder.Services
        .RegisterServices()
        .RegisterDatabase(dbPath);

    return builder.Build();
}