using System.Runtime.CompilerServices;
using Minorag.Cli.Models;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Providers;
using Minorag.Cli.Store;

namespace Minorag.Cli.Services;

public interface ISearcher
{
    Task<SearchContext> RetrieveAsync(
        string question,
        bool verbose,
        List<int>? repositoryIds = null,
        int topK = 7,
        CancellationToken ct = default);

    Task<SearchResult> AnswerAsync(
        SearchContext context,
        bool useLlm = true,
        CancellationToken ct = default);

    IAsyncEnumerable<string> AnswerStreamAsync(
        SearchContext context,
        bool useLlm = true,
        CancellationToken ct = default);
}

public class Searcher(
    ISqliteStore store,
    IEmbeddingProvider embeddingProvider,
    ILlmClient llmClient) : ISearcher
{
    private static readonly string[] PathExtensions =
       [
           ".cs", ".csproj", ".sln",
            ".ts", ".tsx", ".js", ".mjs", ".cjs",
            ".json", ".yaml", ".yml", ".toml", ".md",
            ".tf", ".hcl", ".sh", ".bat", ".ps1",
            ".dockerfile"
       ];

    private static readonly string[] PathLikeNames =
    [
        "dockerfile", "makefile", "readme", "license"
    ];

    public async Task<SearchContext> RetrieveAsync(
    string question,
    bool verbose,
    List<int>? repositoryIds = null,
    int topK = 7,
    CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question is empty.", nameof(question));
        }

        // Optional path hint extracted from the question text
        var pathHint = ExtractPathHint(question);

        // 1. Embed the question
        var queryEmbedding = await embeddingProvider.EmbedAsync(question, ct);

        // 2. Score all chunks by cosine similarity
        var scored = new List<(CodeChunk Chunk, float Score)>();

        await foreach (var chunk in store.GetAllChunksAsync(verbose, repositoryIds, ct))
        {
            if (chunk.Embedding.Length == 0)
                continue;

            if (chunk.Embedding.Length != queryEmbedding.Length)
                continue;

            var score = CosineSimilarity(queryEmbedding, chunk.Embedding);

            // ---- path-based score boost (soft preference) ----
            var boostedScore = score;

            if (!string.IsNullOrEmpty(pathHint))
            {
                var path = chunk.Path ?? string.Empty;

                if (path.Contains(pathHint, StringComparison.OrdinalIgnoreCase))
                {
                    const float boostFactor = 1.2f;
                    boostedScore = score * boostFactor;

                    if (boostedScore > 1f)
                        boostedScore = 1f;
                }
            }

            scored.Add((chunk, boostedScore));
        }

        if (scored.Count == 0)
        {
            return new SearchContext(question, []);
        }

        var top = scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => new ScoredChunk(s.Chunk, s.Score))
            .ToList();

        return new SearchContext(question, top);
    }

    public async IAsyncEnumerable<string> AnswerStreamAsync(
           SearchContext context,
           bool useLlm = true,
           [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!context.HasResults || !useLlm)
        {
            yield break;
        }

        var contextChunks = context.Chunks
            .Select(t => t.Chunk)
            .ToList();

        await foreach (var piece in llmClient.AskStreamAsync(
                context.Question,
                context.UseAdvancedModel,
                contextChunks,
                ct)
                .WithCancellation(ct))
        {
            yield return piece;
        }
    }

    public async Task<SearchResult> AnswerAsync(
        SearchContext context,
        bool useLlm = true,
        CancellationToken ct = default)
    {
        if (!context.HasResults || !useLlm)
        {
            return new SearchResult(context.Question, context.Chunks, null);
        }

        var contextChunks = context.Chunks
            .Select(t => t.Chunk)
            .ToList();

        var answer = await llmClient.AskAsync(context.Question, contextChunks, ct);

        return new SearchResult(context.Question, context.Chunks, answer);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0f;

        double dot = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var va = a[i];
            var vb = b[i];

            dot += va * vb;
            normA += va * va;
            normB += vb * vb;
        }

        if (normA == 0 || normB == 0)
            return 0f;

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }

    private static string? ExtractPathHint(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return null;

        var tokens = question.Split(
            [' ', '\t', '\r', '\n', '\"', '\'', '(', ')', ',', ';', ':'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in tokens)
        {
            var t = raw.Trim().Trim('"', '\'', '`');

            if (string.IsNullOrWhiteSpace(t))
            {
                continue;
            }

            if (t.Contains('/') || t.Contains('\\'))
            {
                return t;
            }

            foreach (var ext in PathExtensions)
            {
                if (t.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            if (PathLikeNames.Contains(t, StringComparer.OrdinalIgnoreCase))
            {
                return t;
            }
        }

        return null;
    }
}