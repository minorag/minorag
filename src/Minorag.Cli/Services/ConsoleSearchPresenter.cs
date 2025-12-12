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

public partial class ConsoleSearchPresenter(IMinoragConsole console) : IConsoleSearchPresenter
{
    private const string SeparatorColor = "silver";
    private const string CodeColor = "cyan";
    private const string MetaColor = "grey70";
    private const char NewLine = '\n';

    public void PresentRetrieval(SearchContext context, bool verbose)
    {
        if (!context.HasResults)
        {
            console.WriteMarkupLine("[yellow]No chunks found in the index. Did you run 'index' first?[/]");
            return;
        }

        var top = context.Chunks;

        console.WriteLine();
        console.WriteMarkupLine($"[bold]Top {top.Count} retrieved chunks (by file):[/]");
        console.WriteLine();

        var rank = 1;
        foreach (var scored in top)
        {
            var chunk = scored.Chunk;
            var score = scored.Score;

            var chunkText = $"[grey][[ {rank} ]][/]: [cyan]{console.EscapeMarkup(chunk.Path)}[/]  (score: [green]{score:F3}[/])";

            console.WriteMarkupLine(chunkText);
            rank++;
        }

        if (!verbose)
        {
            return;
        }

        console.WriteLine();
        console.PrintSeparator(SeparatorColor);
        console.WriteMarkupLine("[bold]Context snippets:[/]");
        console.PrintSeparator(SeparatorColor);
        console.WriteLine();

        rank = 1;
        foreach (var scored in top)
        {
            var chunk = scored.Chunk;
            var score = scored.Score;

            console.PrintSeparator();
            console.WriteMarkupLine($"[grey][[ {rank} ]][/]: [cyan]{console.EscapeMarkup(chunk.Path)}[/]  (score: [green]{score:F3}[/])");

            var repoLabel = chunk.Repository?.Name
                            ?? chunk.Repository?.RootPath
                            ?? $"repo #{chunk.RepositoryId}";

            console.WriteMarkupLine($"      repo: [blue]{console.EscapeMarkup(repoLabel)}[/]");

            if (!string.IsNullOrEmpty(chunk.SymbolName))
            {
                console.WriteMarkupLine($"      symbol: [purple]{console.EscapeMarkup(chunk.SymbolName)}[/]");
            }

            console.WriteLine();

            const int maxLines = 40;
            var lines = chunk.Content.Split(NewLine);

            for (var i = 0; i < Math.Min(maxLines, lines.Length); i++)
            {
                console.WriteMarkupLine(lines[i]);
            }

            if (lines.Length > maxLines)
            {
                console.WriteMarkupLine("[grey]... (truncated)[/]");
            }

            rank++;
            AnsiConsole.WriteLine();
        }
    }

    public async Task PresentAnswerStreamingAsync(
     IAsyncEnumerable<string> answerStream,
     CancellationToken ct = default)
    {
        console.WriteLine();

        console.PrintSeparator(SeparatorColor);
        console.WriteMarkupLine("[bold yellow]Answer (streaming)[/]");
        console.PrintSeparator(SeparatorColor);
        console.WriteLine();

        var buffer = new StringBuilder();

        // --- streaming state ---
        var inCodeBlock = false;
        var codeLang = string.Empty;
        var codeLines = new List<string>();

        var inTable = false;
        var tableLines = new List<string>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(MetaColor)
            .StartAsync("Thinking…", async _ =>
            {
                await foreach (var piece in answerStream.WithCancellation(ct))
                {
                    buffer.Append(piece);

                    // Process complete lines from buffer
                    while (true)
                    {
                        var text = buffer.ToString();
                        var newlineIndex = text.IndexOf(NewLine);
                        if (newlineIndex < 0)
                            break;

                        var line = text[..newlineIndex];          // without '\n'
                        buffer.Remove(0, newlineIndex + 1);       // +1 to drop '\n'

                        ProcessLine(line);
                    }
                }
            });

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
            RenderCodeBlock();
        }

        console.WriteLine();

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
                // fall through
            }

            var trimmed = line.TrimStart();

            // Start of code block
            if (trimmed.StartsWith("```"))
            {
                inCodeBlock = true;
                var afterTicks = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
                codeLang = afterTicks;
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
            var markup = MarkdownToSpectreMarkup(line + "\n");
            AnsiConsole.Markup(markup);
        }

        void RenderCodeBlock()
        {
            if (!string.IsNullOrWhiteSpace(codeLang))
            {
                console.WriteCodeLine($"```{codeLang}", MetaColor);
            }
            else
            {
                console.WriteCodeLine("```", MetaColor);
            }

            foreach (var l in codeLines)
            {
                console.WriteCodeLine("    " + l, CodeColor);
            }

            console.WriteCodeLine("```", MetaColor);
            console.WriteLine();
        }

        void RenderTable()
        {
            var tableText = string.Join(NewLine, tableLines);

            if (!TryRenderMarkdownTable(tableText))
            {
                var markup = MarkdownToSpectreMarkup(tableText + "\n");
                AnsiConsole.Markup(markup);
            }
        }
    }

    public void PresentAnswer(SearchResult result, bool showLlm)
    {
        if (!showLlm)
        {
            console.WriteLine();
            console.WriteWarning("LLM call disabled (--no-llm).");

            return;
        }

        if (!result.HasAnswer)
        {
            console.WriteLine();
            console.WriteWarning("No answer was generated.");
            return;
        }

        var answer = result.Answer!;
        AnsiConsole.WriteLine();
        console.PrintSeparator(SeparatorColor);

        console.WriteMarkupLine("[bold yellow]Answer[/]");

        console.PrintSeparator(SeparatorColor);
        AnsiConsole.WriteLine();

        // Detect & render tables
        if (TryRenderMarkdownTable(answer))
            return;

        // fallback → regular formatting
        var markup = MarkdownToSpectreMarkup(answer);
        console.WriteMarkupLine(markup);
        console.WriteLine();
    }

    private bool TryRenderMarkdownTable(string text)
    {
        var lines = text.Split(NewLine);

        var tableLines = new List<string>();
        var inTable = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith('|'))
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
        console.WriteLine();

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