using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Minorag.Core.Configuration;
using Minorag.Core.Models;
using Minorag.Core.Models.Domain;

namespace Minorag.Core.Services;

public interface IPromptFormatter
{
    string Format(SearchContext context, string question);
}

public partial class MarkdownPromptFormatter : IPromptFormatter
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

                var repoLabel = chunk.Repository?.Name
                                ?? chunk.Repository?.RootPath
                                ?? $"repo #{chunk.RepositoryId}";

                var (language, contentForPrompt) = PrepareContent(chunk);

                sb.AppendLine(
                    $"### {rank}. `{chunk.Path}` (repo: `{repoLabel}`, score: {score:F3})");

                if (!string.IsNullOrEmpty(language))
                {
                    sb.AppendLine($"```{language}");
                    sb.AppendLine(contentForPrompt);
                    sb.AppendLine("```");
                }
                else
                {
                    sb.AppendLine("```");
                    sb.AppendLine(contentForPrompt);
                    sb.AppendLine("```");
                }

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

    private static (string Language, string Content) PrepareContent(CodeChunk chunk)
    {
        var language = chunk.Language?.ToLowerInvariant() ?? string.Empty;
        var content = chunk.Content ?? string.Empty;

        if (string.Equals(language, "html", StringComparison.OrdinalIgnoreCase))
        {
            content = StripHtmlToText(content);
            language = string.Empty;
        }

        if (content.Length > DefaultValues.MaxChunkSize)
        {
            content = content[..DefaultValues.MaxChunkSize] + "\n... (truncated)";
        }

        return (language, content);
    }

    private static string StripHtmlToText(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var noTags = CollapseTagsRegex().Replace(input, string.Empty);

        var decoded = WebUtility.HtmlDecode(noTags);

        var normalized = CollapseWhitespaceRegex().Replace(decoded, " ");
        normalized = CollapseEmptyLinesRegex().Replace(normalized, "\n\n");

        return normalized.Trim();
    }

    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex CollapseEmptyLinesRegex();
    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex CollapseWhitespaceRegex();
    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex CollapseTagsRegex();
}