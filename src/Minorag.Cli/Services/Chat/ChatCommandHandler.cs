using Minorag.Cli.Indexing;
using Spectre.Console;

namespace Minorag.Cli.Services.Chat;

public interface IChatCommandHandler
{
    void PrintHelp();
    Task HandleIndexCommand(
           string? clientName,
           string? projectName,
           CancellationToken ct);
}

public class ChatCommandHandler(
    IMinoragConsole console,
    IIndexScopeService indexScopeService,
    IIndexer indexer) : IChatCommandHandler
{
    public async Task HandleIndexCommand(
           string? clientName,
           string? projectName,
           CancellationToken ct)
    {
        var repoRoot = RagEnvironment.GetRepoRootOrCurrent();

        console.WriteMarkupLine("[grey]Indexing:[/] {0}", Markup.Escape(repoRoot.FullName));

        await indexScopeService.EnsureClientProjectRepoAsync(
            repoRoot,
            clientName,
            projectName,
            ct);

        await indexer.IndexAsync(repoRoot.FullName, false, [], ct);
    }

    public void PrintHelp()
    {
        console.WriteMarkupLine("[bold cyan]Minorag Chat[/] - Enter (after typing)=send, Ctrl+C=exit");

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
            console.WriteMarkupLine($"[grey]{c.Cmd}[/] - {c.Desc}");
        }
    }
}
