using Minorag.Core.Models.Domain;
using Minorag.Core.Models.ViewModels;

namespace Minorag.Core.Providers;

public interface IPromptBuilder
{
    /// <summary>
    /// Builds a full prompt (SYSTEM + CONTEXT + QUESTION) from a user question and
    /// a list of indexed code chunks.
    /// </summary>
    string BuildPrompt(
        string question,
        IReadOnlyList<CodeChunkVm> context,
        string? memory = null);
}