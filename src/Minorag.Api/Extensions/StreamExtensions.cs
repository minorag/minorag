using System.Text;

namespace Minorag.Api.Extensions;

public static class StreamExtensions
{
    public static async Task WriteLine(this Stream stream, string s, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(s + "\n"), ct);
    }
}
