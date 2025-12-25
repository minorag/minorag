using System.Runtime.CompilerServices;
using Minorag.Core.Configuration;
using Minorag.Core.Models;
using Minorag.Core.Models.ViewModels;
using Minorag.Core.Providers;
using Minorag.Core.Store;

namespace Minorag.Core.Services;

public interface ISearcher
{
    Task<SearchContext> RetrieveAsync(
        string question,
        bool verbose,
        List<int>? repositoryIds = null,
        int topK = DefaultValues.TopK,
        float[]? memoryEmbedding = null,
        CancellationToken ct = default);

    IAsyncEnumerable<string> AnswerStreamAsync(
        SearchContext context,
        bool useLlm = true,
        string? memorySummary = null,
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
    int topK = DefaultValues.TopK,
    float[]? memoryEmbedding = null,
    CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question is empty.", nameof(question));
        }

        // 1. Base embedding: current question
        var queryEmbedding = await embeddingProvider.EmbedAsync(question.Trim(), ct);

        var pathHint = ExtractPathHint(question);

        BlendMemoryEmbedding(memoryEmbedding, queryEmbedding);

        var scored = new List<(CodeChunkVm Chunk, float Score)>();

        await foreach (var chunk in store.GetAllChunksAsync(repositoryIds, ct))
        {
            if (chunk.Embedding.Length == 0)
            {
                continue;
            }

            var score = CosineSimilarity(chunk.Embedding, queryEmbedding);

            if (pathHint is not null &&
                chunk.Path is not null &&
                chunk.Path.Contains(pathHint, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.05f;
            }

            if (score > 0f)
            {
                scored.Add((chunk, score));
            }
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
           string? memory = null,
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
                memory,
                ct)
                .WithCancellation(ct))
        {
            yield return piece;
        }
    }

    private static void BlendMemoryEmbedding(float[]? memoryEmbedding, float[] queryEmbedding)
    {
        if (memoryEmbedding is { Length: > 0 } mem &&
            mem.Length == queryEmbedding.Length)
        {
            const float alpha = 0.7f;

            for (var i = 0; i < queryEmbedding.Length; i++)
            {
                queryEmbedding[i] =
                    alpha * queryEmbedding[i] +
                    (1 - alpha) * mem[i];
            }

            // Re-normalize
            var norm = MathF.Sqrt(queryEmbedding.Sum(v => v * v));
            if (norm > 0f)
            {
                for (var i = 0; i < queryEmbedding.Length; i++)
                    queryEmbedding[i] /= norm;
            }
        }
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