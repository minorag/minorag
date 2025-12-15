using System.Text;

namespace Minorag.Cli.Cli;

public interface IConsoleInputProvider
{
    Task<string?> ReadMessageAsync(CancellationToken ct);
}

public sealed class ConsoleInputProvider : IConsoleInputProvider
{
    public Task<string?> ReadMessageAsync(CancellationToken ct)
        => Task.FromResult(ReadWithEditingAndPaste(ct));

    private static string? ReadWithEditingAndPaste(CancellationToken ct)
    {
        var buffer = new StringBuilder();
        var cursor = 0;

        // Save initial cursor position for redrawing
        var startLeft = Console.CursorLeft;
        var startTop = Console.CursorTop;

        // Bracketed paste state
        var inPaste = false;

        // ESC sequence parsing state machine for bracketed paste
        var escState = 0; // 0 none, 1 got ESC, 2 got ESC[
        var escSeq = new StringBuilder(); // collects "200~" / "201~" etc.

        // ---- Cursor blink (best-effort) ----
        // This logic is kept for better UX, but might be removed in a simpler implementation
        Timer? blinkTimer = null;
        var cursorVisible = true;

        blinkTimer = new Timer(_ =>
        {
            try
            {
                cursorVisible = !cursorVisible;
                Console.CursorVisible = cursorVisible;
            }
            catch
            {
                // Ignore if terminal doesn't support this
            }
        }, null, 0, 1000);

        Redraw();

        while (!ct.IsCancellationRequested)
        {
            // Use Console.KeyAvailable to check for input without blocking indefinitely
            // This is generally necessary for CancellationToken usage with Console.ReadKey,
            // though the provided implementation uses a blocking ReadKey which is common
            // for simple console apps. We'll stick to the original structure but be aware
            // of the limitations with true non-blocking input and cancellation.
            if (!Console.KeyAvailable)
            {
                // A small delay to yield and check the cancellation token more frequently
                Task.Delay(50, ct).Wait(ct);
                continue;
            }

            var key = Console.ReadKey(intercept: true);

            // Dispose timer and restore cursor visibility on exit or submit
            void DisposeBlink()
            {
                try
                {
                    blinkTimer?.Dispose();
                    Console.CursorVisible = true; // restore default
                }
                catch { /* ignore */ }
            }

            // Ctrl+C (Exiting)
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                DisposeBlink();
                return null;
            }

            // ---- SUBMISSION: Alt+Enter or Ctrl+E ----
            var isAltEnter = key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Alt);
            var isCtrlE = key.Key == ConsoleKey.E && key.Modifiers.HasFlag(ConsoleModifiers.Control);

            if (isAltEnter || isCtrlE)
            {
                Console.WriteLine();
                DisposeBlink();
                return buffer.ToString();
            }

            // ---- Bracketed paste detection (Raw ESC sequences) ----
            if (escState > 0 || key.KeyChar == '\x1b')
            {
                if (escState == 0 && key.KeyChar == '\x1b')
                {
                    escState = 1;
                    escSeq.Clear();
                    continue;
                }

                if (escState == 1)
                {
                    if (key.KeyChar == '[')
                    {
                        escState = 2;
                        continue;
                    }

                    // unknown ESC sequence
                    escState = 0;
                    continue;
                }

                if (escState == 2)
                {
                    // collect until '~'
                    escSeq.Append(key.KeyChar);

                    if (key.KeyChar == '~')
                    {
                        var seq = escSeq.ToString(); // e.g. "200~"
                        if (seq == "200~") inPaste = true;
                        else if (seq == "201~") inPaste = false;

                        escState = 0;
                        continue;
                    }

                    // safety
                    if (escSeq.Length > 16)
                        escState = 0;

                    continue;
                }
            }

            // ---- Editing keys ----
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    if (cursor > 0) cursor--;
                    SetCursor();
                    continue;

                case ConsoleKey.RightArrow:
                    if (cursor < buffer.Length) cursor++;
                    SetCursor();
                    continue;

                case ConsoleKey.Home:
                    cursor = 0;
                    SetCursor();
                    continue;

                case ConsoleKey.End:
                    cursor = buffer.Length;
                    SetCursor();
                    continue;

                case ConsoleKey.Backspace:
                    if (cursor > 0)
                    {
                        buffer.Remove(cursor - 1, 1);
                        cursor--;
                        Redraw();
                    }
                    continue;

                case ConsoleKey.Delete:
                    if (cursor < buffer.Length)
                    {
                        buffer.Remove(cursor, 1);
                        Redraw();
                    }
                    continue;

                case ConsoleKey.Enter:
                    // --- NEW LOGIC: Enter inserts a newline, unless in Paste mode where it's handled below ---
                    if (!inPaste)
                    {
                        InsertChar('\n');
                        Redraw();
                        continue;
                    }
                    break; // Fall through to character handling for paste mode

                default:
                    break;
            }

            // ---- Normal characters ----
            // In Paste mode: terminals may send '\r' or '\n' on their own
            // Treat CR as newline in paste mode.
            if (inPaste && (key.KeyChar == '\r' || key.KeyChar == '\n'))
            {
                InsertChar('\n');
                Redraw();
                continue;
            }

            if (char.IsControl(key.KeyChar))
                continue; // Ignore other control characters

            // Insert regular character (including characters from paste)
            InsertChar(key.KeyChar);
            Redraw();
        }

        return null; // Should only be reached if cancellation is requested

        // ---------------- local helpers (kept from original code) ----------------

        void InsertChar(char ch)
        {
            buffer.Insert(cursor, ch);
            cursor++;
        }

        void Redraw()
        {
            var width = Math.Max(1, Console.BufferWidth);

            // Clear enough area. Multiline paste can be long, so clear based on current length + slack.
            var clearLen = buffer.Length + 64;

            Console.SetCursorPosition(startLeft, startTop);
            ClearCells(width, startLeft, startTop, clearLen);

            Console.SetCursorPosition(startLeft, startTop);
            WriteBufferWithNewlines(buffer);

            SetCursor();
        }

        void SetCursor()
        {
            var width = Math.Max(1, Console.BufferWidth);

            // We need to compute visual cursor position considering '\n' in buffer.
            // We'll walk the buffer from 0..cursor and compute (left, top).
            var left = startLeft;
            var top = startTop;

            for (var i = 0; i < cursor; i++)
            {
                var ch = buffer[i];
                if (ch == '\n')
                {
                    top++;
                    left = 0;
                    continue;
                }

                left++;
                if (left >= width)
                {
                    top++;
                    left = 0;
                }
            }

            top = Math.Min(top, Console.BufferHeight - 1);
            Console.SetCursorPosition(left, top);
        }

        static void WriteBufferWithNewlines(StringBuilder sb)
        {
            // Write exactly what we have, including newlines.
            Console.Write(sb.ToString());
        }

        static void ClearCells(int width, int startLeft, int startTop, int cells)
        {
            var remaining = cells;
            var left = startLeft;
            var top = startTop;

            while (remaining > 0 && top < Console.BufferHeight)
            {
                var spaceOnLine = width - left;
                var n = Math.Min(spaceOnLine, remaining);

                Console.SetCursorPosition(left, top);
                Console.Write(new string(' ', n));

                remaining -= n;
                top++;
                left = 0;
            }
        }
    }
}