using System.Text;
using Minorag.Cli.Models;

namespace Minorag.Cli.Services;

public interface IPromptFormatter
{
    string Format(SearchContext context, string question);
}

public class MarkdownPromptFormatter : IPromptFormatter
{
    public string Format(SearchContext context, string question)
    {
        var sb = new StringBuilder();

        // SYSTEM
        sb.AppendLine("## SYSTEM");
        sb.AppendLine("You are a senior software engineer helping a teammate understand a codebase.");
        sb.AppendLine("Always reference file paths and important symbols when explaining things.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // QUESTION
        sb.AppendLine("## QUESTION");
        sb.AppendLine(question.Trim());
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // CONTEXT
        if (!context.HasResults)
        {
            sb.AppendLine("## CONTEXT");
            sb.AppendLine("_No relevant code snippets were found in the local index._");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## CONTEXT (top matched code snippets)");
            sb.AppendLine();

            var rank = 1;
            foreach (var scored in context.Chunks)
            {
                var chunk = scored.Chunk;
                var score = scored.Score;

                var language = string.IsNullOrWhiteSpace(chunk.Language)
                    ? ""
                    : chunk.Language.ToLowerInvariant();

                var repoLabel = chunk.Repository?.Name
                                ?? chunk.Repository?.RootPath
                                ?? $"repo #{chunk.RepositoryId}";

                sb.AppendLine(
                    $"### {rank}. `{chunk.Path}` (repo: `{repoLabel}`, score: {score:F3})");

                sb.AppendLine($"```{language}");
                sb.AppendLine(chunk.Content);
                sb.AppendLine("```");
                sb.AppendLine();

                rank++;
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## INSTRUCTIONS TO THE MODEL");
        sb.AppendLine("- Use only the context above to answer.");
        sb.AppendLine("- When referencing code, mention file paths and symbols.");
        sb.AppendLine("- If something is unclear or missing from the context, say so explicitly.");
        sb.AppendLine("- Prefer a concise, structured explanation over long prose.");
        sb.AppendLine();

        return sb.ToString();
    }
}