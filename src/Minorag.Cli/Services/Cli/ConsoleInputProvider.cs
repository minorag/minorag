using System.Text;

namespace Minorag.Cli.Services.Cli;

public interface IConsoleInputProvider
{
    Task<string?> ReadMessageAsync(CancellationToken ct);
}

public class ConsoleInputProvider : IConsoleInputProvider
{

    private readonly StringBuilder buffer = new();

    public async Task<string?> ReadMessageAsync(CancellationToken ct)
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
}
