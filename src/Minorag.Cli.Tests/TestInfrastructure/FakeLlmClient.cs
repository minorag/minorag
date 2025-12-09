using Minorag.Cli.Models.Domain;
using Minorag.Cli.Providers;

namespace Minorag.Cli.Tests.TestInfrastructure;

public class FakeLlmClient : ILlmClient
{
    public bool WasCalled { get; private set; }
    public string? LastQuestion { get; private set; }
    public IReadOnlyList<CodeChunk>? LastContext { get; private set; }
    public string AnswerToReturn { get; set; } = string.Empty;

    public Task<string> AskAsync(
        string question,
        IReadOnlyList<CodeChunk> context,
        CancellationToken ct = default)
    {
        WasCalled = true;
        LastQuestion = question;
        LastContext = context;
        return Task.FromResult(AnswerToReturn);
    }

    public IAsyncEnumerable<string> AskStreamAsync(
        string question,
        bool useAdvancedModel,
        IReadOnlyList<CodeChunk> context,
        CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
