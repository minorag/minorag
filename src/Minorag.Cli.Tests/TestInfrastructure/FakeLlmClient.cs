using Minorag.Core.Models.Domain;
using Minorag.Core.Models.ViewModels;
using Minorag.Core.Providers;

namespace Minorag.Cli.Tests.TestInfrastructure;

public class FakeLlmClient : ILlmClient
{
    public bool WasCalled { get; private set; }
    public string? LastQuestion { get; private set; }
    public IReadOnlyList<CodeChunk>? LastContext { get; private set; }
    public string AnswerToReturn { get; set; } = string.Empty;

    public IAsyncEnumerable<string> AskStreamAsync(
        string question,
        bool useAdvancedModel,
        IReadOnlyList<CodeChunkVm> context,
        string? memory,
        CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
