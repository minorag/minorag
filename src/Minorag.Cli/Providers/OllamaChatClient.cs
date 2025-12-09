using System.Text;
using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Models.Options;

namespace Minorag.Cli.Providers;

public class OllamaChatClient(IOllamaClient ollama, IOptions<OllamaOptions> options) : ILlmClient
{
    private readonly string _model = options.Value.ChatModel;
    private readonly double _temperature = options.Value.Temperature;

    public async Task<string> AskAsync(
        string question,
        IReadOnlyList<CodeChunk> context,
        CancellationToken ct)
    {
        var userPrompt = BuildPrompt(question, context);
        return await ollama.ChatAsync(_model, _temperature, userPrompt, ct);
    }

    public IAsyncEnumerable<string> AskStreamAsync(
        string question,
        IReadOnlyList<CodeChunk> context,
        CancellationToken ct)
    {
        var userPrompt = BuildPrompt(question, context);
        return ollama.ChatStreamAsync(_model, _temperature, userPrompt, ct);
    }

    private static string BuildPrompt(string question, IReadOnlyList<CodeChunk> context)
    {
        var contextText = BuildContextBlock(context);

        var userPrompt = new StringBuilder()
            .AppendLine("You are a senior software engineer helping a teammate work with the codebase.")
            .AppendLine("=== CONTEXT START ===")
            .AppendLine(contextText)
            .AppendLine("=== CONTEXT END ===")
            .AppendLine()
            .AppendLine("Question:")
            .AppendLine(question)
            .ToString();

        return userPrompt;
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