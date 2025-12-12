using Minorag.Cli.Models.Domain;

namespace Minorag.Cli.Providers;

public interface IPromptBuilder
{
    /// <summary>
    /// Builds a full prompt (SYSTEM + CONTEXT + QUESTION) from a user question and
    /// a list of indexed code chunks.
    /// </summary>
    string BuildPrompt(
        string question,
        IReadOnlyList<CodeChunk> context,
        string? memory = null);
}