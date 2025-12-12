using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Models.Options;

namespace Minorag.Cli.Providers;

public class OllamaChatClient(
    IOllamaClient ollama,
    IOptions<OllamaOptions> options,
    IPromptBuilder promptBuilder) : ILlmClient
{
    private readonly string _model = options.Value.ChatModel;
    private readonly string _advancedModel = options.Value.AdvancedChatModel;
    private readonly double _temperature = options.Value.Temperature;

    public IAsyncEnumerable<string> AskStreamAsync(
        string question,
        bool useAdvancedModel,
        IReadOnlyList<CodeChunk> context,
        string? memorySummary,
        CancellationToken ct)
    {
        var prompt = promptBuilder.BuildPrompt(question, context, memorySummary);
        var model = useAdvancedModel ? _advancedModel : _model;
        return ollama.ChatStreamAsync(model, _temperature, prompt, ct);
    }
}