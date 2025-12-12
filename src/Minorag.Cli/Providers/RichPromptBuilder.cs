using System.Text;
using Minorag.Cli.Models.Domain;

namespace Minorag.Cli.Providers;

public sealed class RichPromptBuilder(string systemPrompt = RichPromptBuilder.DefaultSystemPrompt) : IPromptBuilder
{
    private const int PromptBudgetTokens = 120_000;
    private const int DefaultMaxSnippetLength = 2000;

    private const string DefaultSystemPrompt = @"
        ## SYSTEM
        You are a senior software engineer helping a teammate understand a codebase.
        Rules:
        - Do not invent symbols, files, or behavior not present in CONTEXT.
        - If something is missing, say what is missing and what file/symbol would be needed.
        - Prefer pointing to exact file paths and symbol names from CONTEXT.
        - When unsure, ask for the next most relevant file or chunk.
        ";

    private const string Separator = "---";

    private readonly string _systemPrompt = systemPrompt;

    public string BuildPrompt(
        string question,
        IReadOnlyList<CodeChunk> context,
        string? memory = null)
    {
        // Start with “best effort” settings, then tighten until it fits.
        var plan = new FitPlan(
            MemoryTailChars: 24_000,
            TakeChunks: context.Count,
            SnippetChars: DefaultMaxSnippetLength);

        foreach (var candidate in BuildCandidates(plan, context.Count))
        {
            var prompt = Render(question, context, memory, candidate);
            if (FitsBudget(prompt))
                return prompt;
        }

        var fallback = new FitPlan(
            MemoryTailChars: 0,
            TakeChunks: Math.Min(1, context.Count),
            SnippetChars: 200);

        return Render(question, context, memory, fallback);
    }

    private string Render(
        string question,
        IReadOnlyList<CodeChunk> context,
        string? memory,
        FitPlan plan)
    {
        var sb = new StringBuilder();

        // SYSTEM
        sb.AppendLine(_systemPrompt.Trim());
        sb.AppendLine();
        sb.AppendLine(Separator);
        sb.AppendLine();

        AppendMemory(memory, plan.MemoryTailChars, sb);

        AppendContext(context, plan.TakeChunks, plan.SnippetChars, sb);

        sb.AppendLine(Separator);
        sb.AppendLine();

        // QUESTION
        AppendQuestion(question, sb);

        return sb.ToString().TrimEnd();
    }

    private static void AppendQuestion(string question, StringBuilder sb)
    {
        sb.AppendLine("## QUESTION");
        sb.AppendLine(question.Trim());
    }

    private static void AppendMemory(string? memory, int memoryTailChars, StringBuilder sb)
    {
        if (memoryTailChars <= 0)
            return;

        var trimmed = memory?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        if (trimmed.Length > memoryTailChars)
        {
            // Keep the tail (most recent part of memory)
            trimmed = trimmed[^memoryTailChars..];
        }

        sb.AppendLine("## MEMORY");
        sb.AppendLine(trimmed);
        sb.AppendLine();
        sb.AppendLine(Separator);
        sb.AppendLine();
    }

    private static void AppendContext(
        IReadOnlyList<CodeChunk> context,
        int takeChunks,
        int snippetChars,
        StringBuilder sb)
    {
        sb.AppendLine("## CONTEXT (top matched code chunks)");
        sb.AppendLine();

        if (context.Count == 0 || takeChunks <= 0)
        {
            sb.AppendLine("_No relevant code snippets were found in the local index._");
            sb.AppendLine();
            return;
        }

        var rank = 1;
        foreach (var chunk in context.Take(takeChunks))
        {
            sb.AppendLine($"### {rank}. `{chunk.Path}`");
            sb.AppendLine();
            sb.AppendLine($"- Language: `{chunk.Language}`");
            sb.AppendLine($"- Extension: `{chunk.Extension}`");
            sb.AppendLine($"- Kind: `{chunk.Kind}`");
            sb.AppendLine($"- Symbol: `{chunk.SymbolName ?? "(none)"}`");
            sb.AppendLine($"- ChunkIndex: `{chunk.ChunkIndex}`");
            sb.AppendLine();

            var content = TrimSnippet(chunk.Content, snippetChars);

            sb.AppendLine($"```{chunk.Language}");
            sb.AppendLine(content);
            sb.AppendLine("```");
            sb.AppendLine();

            rank++;
        }
    }

    private static string TrimSnippet(string? content, int maxChars)
    {
        if (string.IsNullOrEmpty(content) || maxChars <= 0)
            return string.Empty;

        if (content.Length <= maxChars)
            return content;

        return content[..maxChars] + "\n…(truncated)…";
    }

    private static bool FitsBudget(string prompt) => EstimateTokens(prompt) <= PromptBudgetTokens;

    private static int EstimateTokens(string s) => (s.Length + 3) / 4;

    private static IEnumerable<FitPlan> BuildCandidates(FitPlan start, int contextCount)
    {
        yield return start;

        foreach (var mem in new[] { 12_000, 8_000, 4_000, 2_000, 0 })
            yield return start with { MemoryTailChars = mem };

        var k = Math.Min(start.TakeChunks, contextCount);
        while (k > 1)
        {
            k = Math.Max(1, k / 2);
            yield return start with { TakeChunks = k };
        }

        foreach (var snip in new[] { 1200, 800, 400, 200 })
            yield return start with { TakeChunks = Math.Min(1, contextCount), SnippetChars = snip, MemoryTailChars = 0 };
    }

    private readonly record struct FitPlan(int MemoryTailChars, int TakeChunks, int SnippetChars);
}