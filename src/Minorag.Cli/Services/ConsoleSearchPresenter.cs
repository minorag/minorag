using Minorag.Cli.Models;
using Spectre.Console;

namespace Minorag.Cli.Services;

public interface IConsoleSearchPresenter
{
    void PresentRetrieval(SearchContext context, bool verbose);
    void PresentAnswer(SearchResult result, bool showLlm);
}

public class ConsoleSearchPresenter : IConsoleSearchPresenter
{
    public void PresentRetrieval(SearchContext context, bool verbose)
    {
        if (!context.HasResults)
        {
            AnsiConsole.MarkupLine("[yellow]No chunks found in the index. Did you run 'index' first?[/]");
            return;
        }

        var top = context.Chunks;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Top {top.Count} retrieved chunks (by file):[/]");
        AnsiConsole.WriteLine();

        var rank = 1;
        foreach (var scored in top)
        {
            var chunk = scored.Chunk;
            var score = scored.Score;

            AnsiConsole.MarkupLine(
                $"[grey][[ {rank} ]][/]: [cyan]{EscapeMarkup(chunk.Path)}[/]  (score: [green]{score:F3}[/])");
            rank++;
        }

        if (!verbose)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]" + new string('-', 80) + "[/]");
        AnsiConsole.MarkupLine("[bold]Context snippets:[/]");
        AnsiConsole.MarkupLine("[grey]" + new string('-', 80) + "[/]");
        AnsiConsole.WriteLine();

        rank = 1;
        foreach (var scored in top)
        {
            var chunk = scored.Chunk;
            var score = scored.Score;

            AnsiConsole.MarkupLine("[grey]" + new string('-', 80) + "[/]");
            AnsiConsole.MarkupLine(
                $"[grey][[ {rank} ]][/]: [cyan]{EscapeMarkup(chunk.Path)}[/]  (score: [green]{score:F3}[/])");

            var repoLabel = chunk.Repository?.Name
                            ?? chunk.Repository?.RootPath
                            ?? $"repo #{chunk.RepositoryId}";

            AnsiConsole.MarkupLine($"      repo: [blue]{EscapeMarkup(repoLabel)}[/]");

            if (!string.IsNullOrEmpty(chunk.SymbolName))
            {
                AnsiConsole.MarkupLine($"      symbol: [purple]{EscapeMarkup(chunk.SymbolName)}[/]");
            }

            AnsiConsole.WriteLine();

            const int maxLines = 40;
            var lines = chunk.Content.Split('\n');

            for (var i = 0; i < Math.Min(maxLines, lines.Length); i++)
            {
                AnsiConsole.WriteLine(lines[i]);
            }

            if (lines.Length > maxLines)
            {
                AnsiConsole.MarkupLine("[grey]... (truncated)[/]");
            }

            rank++;
            AnsiConsole.WriteLine();
        }
    }

    public void PresentAnswer(SearchResult result, bool showLlm)
    {
        if (!showLlm)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]LLM call disabled (--no-llm).[/]");
            return;
        }

        if (!result.HasAnswer)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]No answer was generated.[/]");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]" + new string('=', 80) + "[/]");
        AnsiConsole.WriteLine();

        var safe = Markup.Escape(result.Answer!);
        var panel = new Panel(new Markup(safe))
        {
            Header = new PanelHeader("[bold yellow]Answer[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 1, 1, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string EscapeMarkup(string text)
    {
        return text is null ? string.Empty : Markup.Escape(text);
    }
}