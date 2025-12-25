using Minorag.Core.Models.Domain;
using Minorag.Core.Models.ViewModels;

namespace Minorag.Core.Providers;

public interface ILlmClient
{
    IAsyncEnumerable<string> AskStreamAsync(
        string question,
        bool useAdvancedModel,
        IReadOnlyList<CodeChunkVm> context,
        string? memorySummary,
        CancellationToken ct);
}