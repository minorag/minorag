using System.Text;
using Minorag.Cli.Extensions;
using Minorag.Cli.Services.Cli;
using Spectre.Console;

namespace Minorag.Cli.Services.Chat;

public interface IChat
{
    Task RunLoopAsync(
      List<int> repositoryIds,
      int topK,
      bool verbose,
      bool noLlm,
      bool useAdvancedModel,
      CancellationToken ct);
}

public class Chat(
    IChatCommandHandler commandHandler,
    IConsoleInputProvider inputProvider,
    IMinoragConsole console,
    ISearcher searcher,
    IConversation memory,
    IConsoleSearchPresenter presenter) : IChat
{
    public async Task RunLoopAsync(
       List<int> repositoryIds,
       int topK,
       bool verbose,
       bool noLlm,
       bool useAdvancedModel,
       CancellationToken ct)
    {
        var currentTopK = topK;
        var currentUseAdvancedModel = useAdvancedModel;

        console.EnableBracketedPaste();

        try
        {
            commandHandler.PrintHelp();

            while (true)
            {
                console.DrawPrompt();

                var input = await inputProvider.ReadMessageAsync(ct);
                if (input is null)
                {
                    console.WriteMarkupLine("\n[red]✖ Exiting…[/]");
                    return;
                }

                var question = input.Trim();
                if (string.IsNullOrEmpty(question))
                {
                    continue;
                }

                if (question.StartsWith('/'))
                {
                    if (question.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                    {
                        console.WriteMarkupLine("\n[red]✖ Bye-bye...[/]");
                        break;
                    }

                    await TryHandleSlashCommand(question, memory, ct);
                    continue;
                }

                await AskModel(
                    repositoryIds,
                    currentTopK,
                    verbose,
                    noLlm,
                    currentUseAdvancedModel,
                    question,
                    ct);
            }
        }
        finally
        {
            console.DisableBracketedPaste();
        }

        async Task<bool> TryHandleSlashCommand(
            string input,
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
                    console.WriteMarkupLine("[grey]Conversation memory cleared.[/]");
                    return true;

                case "/index":
                    await commandHandler.HandleIndexCommand(null, null, ct);
                    return true;
                case "/deep":
                    currentUseAdvancedModel = true;
                    console.WriteMarkupLine("[yellow] Using advanced model for answers.[/]");
                    return true;
                case "/standard":
                    currentUseAdvancedModel = false;
                    console.WriteMarkupLine("[green] Using default model for answers.[/]");
                    return true;
                case "/help":
                    commandHandler.PrintHelp();
                    return true;
                case "/k":
                    if (split.Length == 2 && int.TryParse(split[1], out var parsedK))
                    {
                        currentTopK = parsedK;
                        console.WriteSuccess($"Top K is updated to {currentTopK}.");
                        return true;
                    }

                    console.WriteError("Unable to parse command arguments try: /k <number/>");
                    return false;
                default:
                    return false;
            }
        }
    }

    private async Task AskModel(
        List<int> repositoryIds,
        int topK,
        bool verbose,
        bool noLlm,
        bool useAdvancedModel,
        string question,
        CancellationToken ct)
    {
        float[]? memoryEmbedding = null;

        try
        {
            memoryEmbedding = await memory.GetCombinedEmbedding(ct);
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                console.WriteMarkupLine("[grey]Memory embedding skipped:[/] {0}", Markup.Escape(ex.Message));
            }
        }

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

            var answer = searcher.AnswerStreamAsync(context, true, memorySummary, ct);
            var teed = answer.Tee(answerBuffer, ct);

            await presenter.PresentAnswerStreamingAsync(teed, ct);

            if (answerBuffer.Length > 0)
                memory.AddTurn(question, answerBuffer.ToString());
        }
    }
}
