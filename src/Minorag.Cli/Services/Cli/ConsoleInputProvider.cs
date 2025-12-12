using System.Text;

namespace Minorag.Cli.Services.Cli;

public interface IConsoleInputProvider
{
    Task<string?> ReadMessageAsync(CancellationToken ct);
}

public sealed class ConsoleInputProvider : IConsoleInputProvider
{
    public Task<string?> ReadMessageAsync(CancellationToken ct)
    {
        // ReadKey is sync; wrap in Task to satisfy interface.
        // If you want true async, you’d need raw terminal IO / platform code.
        return Task.FromResult(ReadLineWithEditing(ct));
    }

    private static string? ReadLineWithEditing(CancellationToken ct)
    {
        var buffer = new StringBuilder();
        var cursor = 0; // cursor index inside buffer [0..Length]

        // Capture where user input starts (prompt already printed by caller).
        var startLeft = Console.CursorLeft;
        var startTop = Console.CursorTop;

        Redraw();

        while (!ct.IsCancellationRequested)
        {
            var key = Console.ReadKey(intercept: true);

            // Ctrl+C
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                return null;

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return buffer.ToString();

                case ConsoleKey.LeftArrow:
                    if (cursor > 0) cursor--;
                    SetCursor();
                    break;

                case ConsoleKey.RightArrow:
                    if (cursor < buffer.Length) cursor++;
                    SetCursor();
                    break;

                case ConsoleKey.Home:
                    cursor = 0;
                    SetCursor();
                    break;

                case ConsoleKey.End:
                    cursor = buffer.Length;
                    SetCursor();
                    break;

                case ConsoleKey.Backspace:
                    if (cursor > 0 && buffer.Length > 0)
                    {
                        buffer.Remove(cursor - 1, 1);
                        cursor--;
                        Redraw();
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursor < buffer.Length)
                    {
                        buffer.Remove(cursor, 1);
                        Redraw();
                    }
                    break;

                default:
                    // Ignore other control keys (arrows handled above)
                    if (char.IsControl(key.KeyChar))
                        break;

                    // Insert at cursor
                    buffer.Insert(cursor, key.KeyChar);
                    cursor++;
                    Redraw();
                    break;
            }
        }

        return null;

        // ---- local helpers ----

        void Redraw()
        {
            // Clear previously drawn text (handles simple wrapping)
            var width = Math.Max(1, Console.BufferWidth);

            // How many console cells did we occupy last time?
            // (We can’t know the “last time” length without storing it;
            // so clear generously based on current buffer length + 32 padding.)
            var clearLen = buffer.Length + 32;

            // Move to start
            Console.SetCursorPosition(startLeft, startTop);

            // Clear enough cells across wrapped lines
            ClearCells(width, startLeft, startTop, clearLen);

            // Move to start and write new buffer
            Console.SetCursorPosition(startLeft, startTop);
            Console.Write(buffer.ToString());

            SetCursor();
        }

        void SetCursor()
        {
            var width = Math.Max(1, Console.BufferWidth);

            // Compute cursor console position with wrapping
            var absolute = startLeft + cursor;
            var left = absolute % width;
            var top = startTop + (absolute / width);

            // Clamp top to buffer (defensive)
            top = Math.Min(top, Console.BufferHeight - 1);

            Console.SetCursorPosition(left, top);
        }

        static void ClearCells(int width, int startLeft, int startTop, int cells)
        {
            var remaining = cells;
            var left = startLeft;
            var top = startTop;

            while (remaining > 0)
            {
                var spaceOnLine = width - left;
                var n = Math.Min(spaceOnLine, remaining);

                Console.SetCursorPosition(left, top);
                Console.Write(new string(' ', n));

                remaining -= n;
                top++;
                left = 0;

                if (top >= Console.BufferHeight)
                    break;
            }
        }
    }
}