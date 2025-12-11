using Minorag.Cli.Models.Domain;

namespace Minorag.Cli.Providers;

public interface ILlmClient
{
    IAsyncEnumerable<string> AskStreamAsync(
        string question,
        bool useAdvancedModel,
        IReadOnlyList<CodeChunk> context,
        string? memorySummary,
        CancellationToken ct);
}