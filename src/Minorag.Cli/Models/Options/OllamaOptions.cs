namespace Minorag.Cli.Models.Options;

public sealed record OllamaOptions
{
    public string Host { get; init; } = "http://127.0.0.1:11434";
    public string EmbeddingModel { get; init; } = "mxbai-embed-large";
    public string ChatModel { get; init; } = "gpt-oss:20b";
    public string AdvancedChatModel { get; init; } = "gemma3:27b";
    public double Temperature { get; init; } = 0.1;
}

