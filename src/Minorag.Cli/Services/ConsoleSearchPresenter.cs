using System.Text.RegularExpressions;
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

        var answer = result.Answer!;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]" + new string('=', 80) + "[/]");
        AnsiConsole.MarkupLine("[bold yellow]Answer[/]");
        AnsiConsole.MarkupLine("[grey]" + new string('=', 80) + "[/]");
        AnsiConsole.WriteLine();

        // Detect & render tables
        if (TryRenderMarkdownTable(answer))
            return;

        // fallback → regular formatting
        var markup = MarkdownToSpectreMarkup(answer);
        AnsiConsole.MarkupLine(markup);
        AnsiConsole.WriteLine();
    }

    private static bool TryRenderMarkdownTable(string text)
    {
        var lines = text.Split('\n');

        var tableLines = new List<string>();
        var inTable = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("|"))
            {
                inTable = true;
                tableLines.Add(line);
            }
            else if (inTable)
            {
                // first non-table line after table block → stop
                break;
            }
        }

        if (tableLines.Count < 2)
            return false;

        // Header row
        var headerParts = tableLines[0]
            .Trim()
            .Trim('|')
            .Split('|')
            .Select(h => h.Trim())
            .ToList();

        // Data rows (skip header + separator row)
        var rowLines = tableLines.Skip(2).ToList();
        var rows = rowLines
            .Select(row => row.Trim().Trim('|')
                .Split('|')
                .Select(c => c.Trim())
                .ToList())
            .ToList();

        var table = new Table
        {
            Border = TableBorder.Rounded
        };

        // Headers (make sure they’re bold)
        foreach (var head in headerParts)
        {
            var headerText = FormatTableCell(head, isHeader: true);
            var column = new TableColumn(headerText)
            {
                Padding = new Padding(1, 0, 1, 0)
            };
            table.AddColumn(column);
        }

        // Rows
        foreach (var row in rows)
        {
            var cells = row
                .Concat(Enumerable.Repeat(string.Empty, headerParts.Count))
                .Take(headerParts.Count)
                .Select(c => FormatTableCell(c, isHeader: false))
                .ToArray();

            table.AddRow(cells);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        return true;
    }

    /// <summary>
    /// Simple Markdown → Spectre markup for table cells:
    /// - **bold**
    /// - *italic*
    /// - `code` (colored)
    /// - <br> → newline
    /// </summary>
    private static string FormatTableCell(string text, bool isHeader)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Replace("\r\n", "\n");

        // HTML line breaks → real newlines
        t = Regex.Replace(t, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Inline code: `IOcrClient` → [cyan]IOcrClient[/]
        t = Regex.Replace(t, "`([^`]+)`", "[cyan]$1[/]");

        // Bold: **External service clients** → [bold]External service clients[/]
        t = Regex.Replace(t, @"\*\*(.+?)\*\*", "[bold]$1[/]");

        // Italic: *text* → [italic]text[/]
        t = Regex.Replace(
            t,
            @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)",
            "[italic]$1[/]"
        );

        // Make headers bold if they aren't already
        if (isHeader && !t.Contains("[bold]"))
        {
            t = "[bold]" + t + "[/]";
        }

        return t;
    }

    private static string EscapeMarkup(string text)
        => text is null ? string.Empty : Markup.Escape(text);

    /// <summary>
    /// Very small Markdown → Spectre markup converter.
    /// Intentionally conservative: supports headings, **bold**, *italic*, inline code, and bullet points.
    /// </summary>
    private static string MarkdownToSpectreMarkup(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var text = markdown.Replace("\r\n", "\n");

        // HTML line breaks → real newlines
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Headings: #, ##, ### → bold/underline variants
        text = Regex.Replace(text, @"^###\s+(.+)$", "[bold underline]$1[/]", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^##\s+(.+)$", "[bold underline]$1[/]", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^#\s+(.+)$", "[bold underline]$1[/]", RegexOptions.Multiline);

        // Bold: **text** → [bold]text[/]
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "[bold]$1[/]");

        // Italic: *text* → [italic]text[/]
        text = Regex.Replace(
            text,
            @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)",
            "[italic]$1[/]"
        );

        // Inline code: `code`
        text = Regex.Replace(text, "`([^`]+)`", "[grey]`$1`[/]");

        // Bullet points: - foo / * foo → • foo
        text = Regex.Replace(text, @"^(\s*)- ", "$1• ", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^(\s*)\* ", "$1• ", RegexOptions.Multiline);

        return text;
    }
}