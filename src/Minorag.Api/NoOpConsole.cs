using Minorag.Core.Services;

namespace Minorag.Api;

public class NoOpConsole : IMinoragConsole
{
    public void DisableBracketedPaste()
    {
        // no-op
    }

    public void DrawPrompt()
    {
        // no-op

    }

    public void EnableBracketedPaste()
    {
        // no-op

    }

    public void EraseChar()
    {
        // no-op

    }

    public string EscapeMarkup(string text)
    {
        return text;
    }

    public void PrintSeparator(string? color = null)
    {
        // no-op
    }

    public void WriteCodeLine(string text, string? color = null)
    {
        // no-op
    }

    public void WriteError(Exception ex)
    {
        // no-op
    }

    public void WriteError(string error)
    {
        // no-op
    }

    public void WriteLine()
    {
        // no-op
    }

    public void WriteMarkupLine(string format, params object[] args)
    {
        // no-op
    }

    public void WritePlainLine(string text)
    {
        // no-op
    }

    public void WriteSuccess(string text)
    {
        // no-op
    }

    public void WriteWarning(string text)
    {
        // no-op
    }
}
