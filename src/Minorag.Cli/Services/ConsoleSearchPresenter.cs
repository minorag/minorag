using System.Text;
using Minorag.Cli.Models;
using Spectre.Console;

namespace Minorag.Cli.Services;

public interface IConsoleSearchPresenter
{
    void PresentRetrieval(SearchContext context, bool verbose);
    void PresentAnswer(SearchResult result, bool showLlm);
    Task PresentAnswerStreamingAsync(
       IAsyncEnumerable<string> answerStream,
       CancellationToken ct = default);
}

public partial class ConsoleSearchPresenter : IConsoleSearchPresenter
{
    private const string SeparatorColor = "silver";  // lighter than grey
    private const string CodeColor = "cyan";         // for inline code
    private const string MetaColor = "grey70";

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

    public async Task PresentAnswerStreamingAsync(
        IAsyncEnumerable<string> answerStream,
        CancellationToken ct = default)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{SeparatorColor}]" + new string('=', 80) + "[/]");
        AnsiConsole.MarkupLine("[bold yellow]Answer (streaming)[/]");
        AnsiConsole.MarkupLine($"[{SeparatorColor}]" + new string('=', 80) + "[/]");
        AnsiConsole.WriteLine();

        var buffer = new StringBuilder();

        // --- streaming state ---
        var inCodeBlock = false;
        var codeLang = string.Empty;
        var codeLines = new List<string>();

        var inTable = false;
        var tableLines = new List<string>();

        // tiny inline spinner state
        var spinnerFrames = new[] { "|", "/", "-", "\\" };
        var spinnerIndex = 0;
        var spinnerVisible = false;

        void TickSpinner()
        {
            var frame = spinnerFrames[spinnerIndex];
            spinnerIndex = (spinnerIndex + 1) % spinnerFrames.Length;

            if (!spinnerVisible)
            {
                AnsiConsole.Markup($"[{MetaColor}]{frame}[/]");
                spinnerVisible = true;
            }
            else
            {
                // erase previous char and draw new one
                AnsiConsole.Write("\b \b");
                AnsiConsole.Markup($"[{MetaColor}]{frame}[/]");
            }
        }

        void ClearSpinner()
        {
            if (!spinnerVisible) return;
            AnsiConsole.Write("\b \b"); // erase char
            spinnerVisible = false;
        }

        // Consume chunks from the LLM stream
        await foreach (var piece in answerStream.WithCancellation(ct))
        {
            buffer.Append(piece);

            if (!buffer.ToString().Contains('\n'))
            {
                TickSpinner();
            }

            // Process complete lines from buffer
            while (true)
            {
                var text = buffer.ToString();
                var newlineIndex = text.IndexOf('\n');
                if (newlineIndex < 0)
                    break;

                var line = text[..newlineIndex];          // without '\n'
                buffer.Remove(0, newlineIndex + 1);       // +1 to drop '\n'

                ProcessLine(line);
            }
        }

        // Flush last partial line, if any
        if (buffer.Length > 0)
        {
            ProcessLine(buffer.ToString());
        }

        // Finalize any open structures
        if (inTable && tableLines.Count > 0)
        {
            RenderTable();
        }
        else if (inCodeBlock && codeLines.Count > 0)
        {
            // Stream ended before closing ``` – just print raw
            RenderCodeBlock();
        }

        ClearSpinner();
        AnsiConsole.WriteLine();

        // ----------------- local helpers -----------------

        void ProcessLine(string line)
        {
            if (inCodeBlock)
            {
                // End of code block?
                if (line.TrimStart().StartsWith("```"))
                {
                    RenderCodeBlock();
                    inCodeBlock = false;
                    codeLang = string.Empty;
                    codeLines.Clear();
                }
                else
                {
                    codeLines.Add(line);
                }

                return;
            }

            if (inTable)
            {
                // Still in table?
                if (line.TrimStart().StartsWith("|"))
                {
                    tableLines.Add(line);
                    return;
                }

                // Table ended → render it, then treat this line as normal
                RenderTable();
                inTable = false;
                tableLines.Clear();
                // fall through to normal handling for current line
            }

            var trimmed = line.TrimStart();

            // Start of code block: ```c#, ```bash, ```md, or just ```
            if (trimmed.StartsWith("```"))
            {
                inCodeBlock = true;
                var afterTicks = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
                codeLang = afterTicks; // may be empty
                return;
            }

            // Start of markdown table
            if (trimmed.StartsWith("|"))
            {
                inTable = true;
                tableLines.Add(line);
                return;
            }

            // Normal markdown → convert + print immediately
            ClearSpinner();
            var markup = MarkdownToSpectreMarkup(line + "\n");
            AnsiConsole.Markup(markup);
        }

        void RenderCodeBlock()
        {
            ClearSpinner();

            // Join buffered lines into a single string first
            var code = string.Join('\n', codeLines);

            // Optional language label above the block
            if (!string.IsNullOrWhiteSpace(codeLang))
            {
                AnsiConsole.MarkupLine($"[{MetaColor}]```{codeLang}[/]");
            }

            // Print each line indented, with markup escaped
            var lines = code.Split('\n');
            foreach (var l in lines)
            {
                var escaped = EscapeMarkup(l);
                AnsiConsole.MarkupLine($"    [{CodeColor}]{escaped}[/]");
            }

            AnsiConsole.MarkupLine($"[{MetaColor}]```[/]");
            AnsiConsole.WriteLine();
        }

        void RenderTable()
        {
            ClearSpinner();

            var tableText = string.Join('\n', tableLines);

            // Reuse your existing table renderer
            if (!TryRenderMarkdownTable(tableText))
            {
                // Fallback: plain markdown formatting
                var markup = MarkdownToSpectreMarkup(tableText + "\n");
                AnsiConsole.Markup(markup);
            }
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

    private static string FormatTableCell(string text, bool isHeader)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Replace("\r\n", "\n");
        t = RegexHelper.HtmlLineBreaksRegex.Replace(t, "\n");

        t = Markup.Escape(t);
        t = ApplyInlineMarkdown(t, forBlock: false);

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

        text = RegexHelper.HtmlLineBreaksRegex.Replace(text, "\n");

        text = Markup.Escape(text);

        text = RegexHelper.HeadingsH3Regex.Replace(text, "[bold underline]$1[/]");
        text = RegexHelper.HeadingsH2Regex.Replace(text, "[bold underline]$1[/]");
        text = RegexHelper.HeadingsH1Regex.Replace(text, "[bold underline]$1[/]");

        text = ApplyInlineMarkdown(text, forBlock: true);

        text = RegexHelper.BulletHyphenRegex.Replace(text, "$1• ");
        text = RegexHelper.BulletStarRegex.Replace(text, "$1• ");

        return text;
    }

    /// <summary>
    /// Shared inline markdown → Spectre pipeline:
    /// - **bold**
    /// - *italic*
    /// - `code` (colored)
    /// Works for both full blocks and table cells.
    /// </summary>
    private static string ApplyInlineMarkdown(string text, bool forBlock)
    {
        text = RegexHelper.InlineCodeRegex.Replace(text, $"[{CodeColor}]$1[/]");
        text = RegexHelper.BoldRegex.Replace(text, "[bold]$1[/]");
        text = RegexHelper.ItalicRegex.Replace(text, "[italic]$1[/]");

        return text;
    }

}