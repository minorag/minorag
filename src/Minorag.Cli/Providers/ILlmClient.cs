using Minorag.Cli.Models.Domain;

namespace Minorag.Cli.Providers;

public interface ILlmClient
{
    Task<string> AskAsync(string question, IReadOnlyList<CodeChunk> context, CancellationToken ct);
    IAsyncEnumerable<string> AskStreamAsync(
        string question,
        IReadOnlyList<CodeChunk> context,
        CancellationToken ct);
}