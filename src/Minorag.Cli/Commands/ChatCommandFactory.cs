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
            Description = "Interactive mode - like `ask`, but in a loop."
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
                    explicitRepoNames,
                    reposCsv,
                    projectName,
                    clientName,
                    allRepos,
                    ct);

                repoIds = repositories.Select(r => r.Id).ToList();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape(ex.Message));
                Environment.ExitCode = 1;
                return;
            }

            var effectiveTopK = parseResult.GetValue(CliOptions.TopKOption) ?? ragOptions.Value.TopK;

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
        var currentTopK = topK;
        var currentUseAdvancedModel = useAdvancedModel;

        EnableBracketedPaste();

        try
        {
            PrintHelp();

            var buffer = new StringBuilder();

            while (true)
            {
                buffer.Clear();
                DrawPrompt();

                var input = await ReadMessageAsync(buffer, ct);
                if (input is null)
                {
                    AnsiConsole.MarkupLine("\n[red]✖ Exiting…[/]");
                    return;
                }

                var question = input.Trim();
                if (string.IsNullOrEmpty(question))
                    continue;

                if (question.StartsWith('/'))
                {
                    if (question.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                    {
                        AnsiConsole.MarkupLine("\n[red]✖ Bye-bye...[/]");
                        break;
                    }

                    await TryHandleSlashCommand(question, provider, memory, ct);
                    continue;
                }

                await AskModel(
                    repositoryIds,
                    currentTopK,
                    verbose,
                    noLlm,
                    currentUseAdvancedModel,
                    memory,
                    searcher,
                    presenter,
                    question,
                    ct);
            }
        }
        finally
        {
            DisableBracketedPaste();
        }

        async Task<bool> TryHandleSlashCommand(
            string input,
            IServiceProvider provider,
            IConversation memory,
            CancellationToken ct)
        {
            var split = input.Trim().ToLowerInvariant().Split(' ');

            if (split.Length == 0)
            {
                return false;
            }

            var command = split[0];

            switch (command)
            {
                case "/clear":
                    memory.Clear();
                    AnsiConsole.MarkupLine("[grey]Conversation memory cleared.[/]");
                    return true;

                case "/index":
                    var indexer = provider.GetRequiredService<IIndexer>();
                    var indexScope = provider.GetRequiredService<IIndexScopeService>();
                    await HandleIndexCommand(indexScope, indexer, null, null, ct);
                    return true;
                case "/deep":
                    currentUseAdvancedModel = true;
                    AnsiConsole.MarkupLine("[yellow] Using advanced model for answers.[/]");
                    return true;
                case "/standard":
                    currentUseAdvancedModel = false;
                    AnsiConsole.MarkupLine("[green] Using default model for answers.[/]");
                    return true;
                case "/help":
                    PrintHelp();
                    return true;
                case "/k":
                    if (split.Length == 2 && int.TryParse(split[1], out var parsedK))
                    {
                        currentTopK = parsedK;
                        AnsiConsole.MarkupLine($"[green]Top K is updated to {currentTopK}.[/]");
                        return true;
                    }

                    AnsiConsole.MarkupLine($"[red] Unable to parse command arguments try: /k <number/>[/]");
                    return false;
                default:
                    return false;
            }
        }
    }

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("[bold cyan]Minorag Chat[/] - Enter (after typing)=send, Ctrl+C=exit");

        var chatCommands = new[]
        {
            new { Cmd = "/clear",   Desc = "Clear the chat history" },
            new { Cmd = "/index",   Desc = "Re-index the current repository" },
            new { Cmd = "/deep",    Desc = "Use the advanced model for answers" },
            new { Cmd = "/standard",Desc = "Use the default model for answers" },
            new { Cmd = "/k <number>", Desc = "Set the top-K value for retrieval" },
            new { Cmd = "/help",    Desc = "Show this help screen" },
            new { Cmd = "/exit",    Desc = "Exit the chat session" }
        };

        // Print each command on its own line
        foreach (var c in chatCommands)
        {
            AnsiConsole.MarkupLine($"[grey]{c.Cmd}[/] - {c.Desc}");
        }
    }

    private static async Task<string?> ReadMessageAsync(StringBuilder buffer, CancellationToken ct)
    {
        buffer.Clear();

        // IMPORTANT: read CHARS, not bytes (avoids UTF-8 multi-byte overflow on paste)
        var inPaste = false;

        // ESC sequence tracking for bracketed paste:
        // start: ESC [ 2 0 0 ~
        // end:   ESC [ 2 0 1 ~
        var escState = 0; // 0=none, 1=got ESC, 2=got ESC[
        var escSeq = new StringBuilder();

        var chBuf = new char[1];

        while (!ct.IsCancellationRequested)
        {
            var n = await Console.In.ReadAsync(chBuf, 0, 1);
            if (n == 0)
                continue;

            var ch = chBuf[0];

            // Ctrl+C (ETX)
            if (ch == (char)3)
                return null;

            // Bracketed paste detection (ASCII escape sequence)
            if (escState > 0 || ch == '\x1b')
            {
                if (escState == 0 && ch == '\x1b')
                {
                    escState = 1;
                    escSeq.Clear();
                    continue;
                }

                if (escState == 1)
                {
                    if (ch == '[')
                    {
                        escState = 2;
                        continue;
                    }

                    escState = 0;
                    // ignore unknown ESC sequences
                    continue;
                }

                if (escState == 2)
                {
                    escSeq.Append(ch);

                    if (ch == '~')
                    {
                        var seq = escSeq.ToString(); // "200~" or "201~"
                        if (seq == "200~") inPaste = true;
                        else if (seq == "201~") inPaste = false;

                        escState = 0;
                        continue;
                    }

                    // safety: if sequence is too long, abort parsing
                    if (escSeq.Length > 16)
                        escState = 0;

                    continue;
                }
            }

            // Backspace (both variants)
            if (ch == '\b' || ch == (char)127)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;

                    // best-effort erase on terminal
                    if (Console.CursorLeft > 0)
                    {
                        Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                        Console.Write(' ');
                        Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                    }
                }
                continue;
            }

            // Enter / newline
            if (ch == '\r' || ch == '\n')
            {
                if (inPaste)
                {
                    // pasted newline => keep in buffer, never send
                    buffer.AppendLine();
                    Console.WriteLine();
                    continue;
                }

                // user pressed Enter outside paste => SEND
                Console.WriteLine();
                return buffer.ToString();
            }

            // normal char
            buffer.Append(ch);
            Console.Write(ch);
        }

        return null;
    }

    private static void EnableBracketedPaste() =>
        Console.Write("\x1b[?2004h");

    private static void DisableBracketedPaste() =>
        Console.Write("\x1b[?2004l");

    private static void DrawPrompt() =>
        AnsiConsole.Markup("[green]> [/]");

    private static async Task AskModel(
        List<int> repositoryIds,
        int topK,
        bool verbose,
        bool noLlm,
        bool useAdvancedModel,
        IConversation memory,
        ISearcher searcher,
        IConsoleSearchPresenter presenter,
        string question,
        CancellationToken ct)
    {
        var memoryEmbedding = await memory.GetCombinedEmbedding(ct);

        var context = await searcher.RetrieveAsync(
            question,
            verbose,
            repositoryIds,
            topK,
            memoryEmbedding,
            ct);

        context.UseAdvancedModel = useAdvancedModel;
        presenter.PresentRetrieval(context, verbose);

        if (!noLlm && context.HasResults)
        {
            var answerBuffer = new StringBuilder();
            var memorySummary = memory.GetPromptMemory(
                    maxChars: 24_000,
                    maxTurns: 8);

            await presenter.PresentAnswerStreamingAsync(
                TeeStream(
                    searcher.AnswerStreamAsync(context, true, memorySummary, ct),
                    answerBuffer,
                    ct),
                ct);

            if (answerBuffer.Length > 0)
                memory.AddTurn(question, answerBuffer.ToString());
        }
    }

    private static async Task HandleIndexCommand(
        IIndexScopeService indexScopeService,
        IIndexer indexer,
        string? clientName,
        string? projectName,
        CancellationToken ct)
    {
        var repoRoot = RagEnvironment.GetRepoRootOrCurrent();

        AnsiConsole.MarkupLine("[grey]Indexing:[/] {0}", Markup.Escape(repoRoot.FullName));

        await indexScopeService.EnsureClientProjectRepoAsync(
            repoRoot,
            clientName,
            projectName,
            ct);

        await indexer.IndexAsync(repoRoot.FullName, false, [], ct);
    }

    private static async IAsyncEnumerable<string> TeeStream(
        IAsyncEnumerable<string> source,
        StringBuilder sink,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var piece in source.WithCancellation(ct))
        {
            if (!string.IsNullOrWhiteSpace(piece))
            {
                sink.Append(piece);
            }

            yield return piece;
        }
    }
}