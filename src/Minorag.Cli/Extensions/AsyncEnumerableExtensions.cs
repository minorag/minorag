using System.Runtime.CompilerServices;
using System.Text;

namespace Minorag.Cli.Extensions;

public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<string> Tee(
        this IAsyncEnumerable<string> source,
        StringBuilder sink,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var piece in source.WithCancellation(ct))
        {
            if (!string.IsNullOrWhiteSpace(piece))
            {
                sink.Append(piece);
            }

            yield return piece;
        }
    }
}
