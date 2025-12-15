namespace Minorag.Core.Services;

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
    void EnableBracketedPaste();
    void DisableBracketedPaste();
}
