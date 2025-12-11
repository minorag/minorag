using System.CommandLine;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Indexing;
using Minorag.Cli.Models;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Services;
using Minorag.Cli.Services.Chat;
using Spectre.Console;

namespace Minorag.Cli.Commands;

public static class ChatCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("chat")
        {
            Description = "Interactive mode – like `ask`, but in a loop."
        };

        cmd.Add(CliOptions.TopKOption);
        cmd.Add(CliOptions.RepoNameOption);
        cmd.Add(CliOptions.RepoNamesCsvOption);
        cmd.Add(CliOptions.ClientOption);
        cmd.Add(CliOptions.ProjectOption);
        cmd.Add(CliOptions.DbOption);
        cmd.Add(CliOptions.VerboseOption);
        cmd.Add(CliOptions.NoLlmOption);
        cmd.Add(CliOptions.AllReposOption);
        cmd.Add(CliOptions.DeepOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            var verbose = parseResult.GetValue(CliOptions.VerboseOption);
            var noLlm = parseResult.GetValue(CliOptions.NoLlmOption);
            var useAdvancedModel = parseResult.GetValue(CliOptions.DeepOption);

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var ragOptions = scope.ServiceProvider.GetRequiredService<IOptions<RagOptions>>();
            var scopeResolver = scope.ServiceProvider.GetRequiredService<ScopeResolver>();

            var explicitRepoNames = parseResult.GetValue(CliOptions.RepoNameOption) ?? [];
            var reposCsv = parseResult.GetValue(CliOptions.RepoNamesCsvOption);
            var projectName = parseResult.GetValue(CliOptions.ProjectOption);
            var clientName = parseResult.GetValue(CliOptions.ClientOption);
            var allRepos = parseResult.GetValue(CliOptions.AllReposOption);

            List<int> repoIds;
            try
            {
                var repositories = await scopeResolver.ResolveScopeAsync(
                    Environment.CurrentDirectory,
                    repoNames: explicitRepoNames,
                    reposCsv: reposCsv,
                    projectName,
                    clientName,
                    allRepos,
                    ct);

                repoIds = [.. repositories.Select(r => r.Id)];
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape(ex.Message.TrimEnd()));
                AnsiConsole.WriteLine();

                Environment.ExitCode = 1;
                return;
            }

            var topKOverride = parseResult.GetValue(CliOptions.TopKOption);
            var effectiveTopK = topKOverride ?? ragOptions.Value.TopK;

            await RunChatLoopAsync(
                scope.ServiceProvider,
                repoIds,
                effectiveTopK,
                verbose,
                noLlm,
                useAdvancedModel,
                ct);
        });

        return cmd;
    }

    private static async Task RunChatLoopAsync(
            IServiceProvider provider,
            List<int> repositoryIds,
            int topK,
            bool verbose,
            bool noLlm,
            bool useAdvancedModel,
            CancellationToken ct)
    {
        var searcher = provider.GetRequiredService<ISearcher>();
        var presenter = provider.GetRequiredService<IConsoleSearchPresenter>();
        var memory = provider.GetRequiredService<IConversation>();

        var buffer = new StringBuilder();

        AnsiConsole.MarkupLine(
            "[bold cyan]Minorag Chat[/] – type your question (Enter to send, Shift+Enter for newline, Ctrl-C to exit).");
        AnsiConsole.MarkupLine("[grey]Commands: /clear (reset chat memory)[/]");

        while (true)
        {
            buffer.Clear();
            DrawPrompt();

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                // Ctrl+C to exit
                if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    AnsiConsole.MarkupLine("\n[red]✖ Exiting…[/]");
                    return;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    HandleBackspace(buffer);

                    continue;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    // Shift+Enter → newline inside the buffer
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                    {
                        buffer.AppendLine();
                        Console.WriteLine();
                        continue;
                    }

                    // Plain Enter → send the message
                    Console.WriteLine();
                    break;
                }

                // Normal character input
                Console.Write(key.KeyChar);
                buffer.Append(key.KeyChar);
            }

            var line = buffer.ToString();
            var question = line.Trim();
            if (string.IsNullOrEmpty(question))
            {
                continue;
            }

            // Slash commands: /clear, /index, ...
            if (question.StartsWith('/'))
            {
                if (await TryHandleSlashCommand(question, provider, memory, ct))
                {
                    continue;
                }

                AnsiConsole.MarkupLine($"[yellow]Unknown command:[/] {Markup.Escape(question)}");
                continue;
            }

            await AskModel(repositoryIds, topK, verbose, noLlm, useAdvancedModel, memory, searcher, presenter, question, ct);
        }

        static void DrawPrompt()
        {
            AnsiConsole.Markup("[green]> [/]");
        }
    }

    private static async Task AskModel(List<int> repositoryIds, int topK, bool verbose, bool noLlm, bool useAdvancedModel, IConversation memory, ISearcher searcher, IConsoleSearchPresenter presenter, string question, CancellationToken ct)
    {
        var memoryEmbedding = await memory.GetCombinedEmbedding(ct);

        var context = await searcher.RetrieveAsync(
            question,
            verbose,
            repositoryIds: repositoryIds,
            topK: topK,
            memoryEmbedding: memoryEmbedding,
            ct: ct);

        context.UseAdvancedModel = useAdvancedModel;

        presenter.PresentRetrieval(context, verbose);

        if (!noLlm && context.HasResults)
        {
            var rawStream = searcher.AnswerStreamAsync(
                context,
                useLlm: true,
                memorySummary: null,
                ct: ct);

            var answerBuffer = new StringBuilder();
            var teeStream = TeeStream(rawStream, answerBuffer, ct);

            await presenter.PresentAnswerStreamingAsync(teeStream, ct);

            var answerText = answerBuffer.ToString();
            if (!string.IsNullOrWhiteSpace(answerText))
            {
                memory.AddTurn(question, answerText);
            }
        }
        else if (noLlm)
        {
            presenter.PresentAnswer(
                new SearchResult(context.Question, context.Chunks, null),
                showLlm: false);
        }
    }

    private static void HandleBackspace(StringBuilder buffer)
    {
        if (buffer.Length > 0)
        {
            buffer.Remove(buffer.Length - 1, 1);

            var left = Console.CursorLeft;
            var top = Console.CursorTop;

            if (left > 0)
            {
                Console.SetCursorPosition(left - 1, top);
                Console.Write(' ');
                Console.SetCursorPosition(left - 1, top);
            }
        }
    }

    private static async Task<bool> TryHandleSlashCommand(
        string input,
        IServiceProvider provider,
        IConversation memory,
        CancellationToken ct)
    {
        var trimmed = input.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var command = firstSpace >= 0
            ? trimmed[..firstSpace]
            : trimmed;

        switch (command.ToLowerInvariant())
        {
            case "/clear":
                memory.Clear();
                AnsiConsole.MarkupLine("[grey]Conversation memory cleared.[/]");
                return true;

            case "/index":
                var indexScopeService = provider.GetRequiredService<IIndexScopeService>();
                var indexer = provider.GetRequiredService<IIndexer>();

                await HandleIndexCommand(indexScopeService, indexer, null, null, ct);
                return true;

            default:
                return false;
        }
    }

    private static async Task HandleIndexCommand(
       IIndexScopeService indexScopeService,
       IIndexer indexer,
       string? clientName,
       string? projectName,
       CancellationToken ct)
    {
        // Same behavior as `index` without extra CLI flags:
        // - repo = current repo root (or current dir)
        // - incremental indexing (reindex: false)
        // - no extra exclude patterns
        var repoRoot = RagEnvironment.GetRepoRootOrCurrent();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[grey]Indexing current repo:[/] [cyan]{Markup.Escape(repoRoot.FullName)}[/]");

        try
        {
            await indexScopeService.EnsureClientProjectRepoAsync(
                repoRoot,
                clientName,
                projectName,
                ct);

            await indexer.IndexAsync(
                repoRoot.FullName,
                reindex: false,
                excludePatterns: [],
                 ct);

            AnsiConsole.MarkupLine("[green]✔ Indexing completed.[/]");
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Indexing was cancelled.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Indexing failed:[/] {0}", Markup.Escape(ex.Message));
        }

        AnsiConsole.WriteLine();
    }

    private static async IAsyncEnumerable<string> TeeStream(
        IAsyncEnumerable<string> source,
        StringBuilder sink,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var piece in source.WithCancellation(ct))
        {
            sink.Append(piece);
            yield return piece;
        }
    }
}