using Minorag.Core.Models.Domain;

namespace Minorag.Core.Providers;

public interface ILlmClient
{
    IAsyncEnumerable<string> AskStreamAsync(
        string question,
        bool useAdvancedModel,
        IReadOnlyList<CodeChunk> context,
        string? memorySummary,
        CancellationToken ct);
}