using System.Globalization;
using Spectre.Console;

namespace Minorag.Cli.Services;

public interface IMinoragConsole
{
    void EraseChar();
    void WriteMarkupLine(string format, params object[] args);
    void WriteCodeLine(string text, string? color = null);
    void WritePlainLine(string text);
    void WriteLine();
    void WriteError(Exception ex);
    void WriteError(string error);
    void WriteSuccess(string text);
    void WriteWarning(string text);
    void PrintSeparator(string? color = null);
    void DrawPrompt();
    string EscapeMarkup(string text);
}

public class MinoragConsole : IMinoragConsole
{
    private static readonly string Separator = new('=', 80);

    public string EscapeMarkup(string text)
    {
        try
        {
            return text is null ? string.Empty : Markup.Escape(text);
        }
        catch
        {
            WriteWarning("Warning: Error parsing markup. Returning plain text");
            return text;
        }
    }

    public void WriteCodeLine(string text, string? color = null)
    {
        var safe = Markup.Escape(text ?? string.Empty);

        if (string.IsNullOrWhiteSpace(color))
            AnsiConsole.MarkupLine(safe);
        else
            AnsiConsole.MarkupLine($"[{color}]{safe}[/]");
    }

    public void WritePlainLine(string text)
    {
        AnsiConsole.WriteLine(text ?? string.Empty);
    }

    public void WriteError(Exception ex)
    {
        WriteLine();
        var error = EscapeMarkup(Markup.Escape(ex.Message.TrimEnd()));
        WriteError(error);
        WriteLine();
    }

    public void WriteError(string error)
    {
        WriteMarkupLine("[red]Error:[/] {0}", error);
    }

    public void WriteWarning(string text)
    {
        WriteLine();
        WriteMarkupLine("[yellow] {0} [/]", text);
        WriteLine();
    }

    public void WriteLine()
    {
        AnsiConsole.WriteLine();
    }

    public void WriteMarkupLine(string format, params object[] args)
    {
        try
        {
            if (args is null || args.Length == 0)
            {
                AnsiConsole.MarkupLine(format);
                return;
            }

            AnsiConsole.MarkupLine(format, args);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            // Last-resort fallback
            try { AnsiConsole.WriteLine(string.Format(format, args)); }
            catch { AnsiConsole.WriteLine(format); }
        }
    }

    public void PrintSeparator(string? color = null)
    {
        var separatorColor = string.IsNullOrWhiteSpace(color) ? "grey" : color;
        var separator = $"[{separatorColor}]{Separator}[/]";
        WriteMarkupLine(separator);
    }

    public void EraseChar()
    {
        AnsiConsole.Write("\b \b");
    }

    public void WriteSuccess(string text)
    {
        WriteMarkupLine($"[green] {text} [/]");
    }

    public void DrawPrompt()
    {
        AnsiConsole.Markup("[green]> [/]");
    }
}
