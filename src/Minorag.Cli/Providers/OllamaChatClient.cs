using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Services;

namespace Minorag.Cli.Providers;

public class OllamaChatClient(
    IOllamaClient ollama,
    IOptions<OllamaOptions> options,
    ITokenCounter counter,
    IMinoragConsole console,
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
        var tokens = counter.CountTokens(prompt);
        console.WriteMarkupLine($"[grey] Input Context: {tokens} tokens. [/]");
        var model = useAdvancedModel ? _advancedModel : _model;
        return ollama.ChatStreamAsync(model, _temperature, prompt, ct);
    }
}