using System.Text;
using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Models.Options;

namespace Minorag.Cli.Providers;

public class OllamaChatClient(IOllamaClient ollama, IOptions<OllamaOptions> options) : ILlmClient
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
        var prompt = BuildPrompt(question, context, memorySummary);
        var model = useAdvancedModel ? _advancedModel : _model;
        return ollama.ChatStreamAsync(model, _temperature, prompt, ct);
    }

    private static string BuildPrompt(
        string question,
        IReadOnlyList<CodeChunk> context,
        string? memory = null)
    {
        var contextText = BuildContextBlock(context);
        var sb = new StringBuilder();

        sb.AppendLine("You are a senior software engineer helping a teammate understand the codebase.");

        if (!string.IsNullOrWhiteSpace(memory))
        {
            sb.AppendLine(memory);
        }

        sb.AppendLine("=== CONTEXT START ===")
          .AppendLine(contextText)
          .AppendLine("=== CONTEXT END ===")
          .AppendLine()
          .AppendLine("Question:")
          .AppendLine(question);

        return sb.ToString();
    }

    private static string BuildContextBlock(IReadOnlyList<CodeChunk> chunks)
    {
        var sb = new StringBuilder();

        foreach (var chunk in chunks)
        {
            sb.AppendLine($"--- File: {chunk.Path} ({chunk.Language}) ---");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}