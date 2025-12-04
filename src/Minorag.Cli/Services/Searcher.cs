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
        int topK = 7,
        CancellationToken ct = default);

    Task<SearchResult> AnswerAsync(
        SearchContext context,
        bool useLlm = true,
        CancellationToken ct = default);
}

public class Searcher(
    ISqliteStore store,
    IEmbeddingProvider embeddingProvider,
    ILlmClient llmClient) : ISearcher
{
    public async Task<SearchContext> RetrieveAsync(
        string question,
        bool verbose,
        int topK = 7,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question is empty.", nameof(question));
        }

        // 1. Embed the question
        var queryEmbedding = await embeddingProvider.EmbedAsync(question, ct);

        // 2. Score all chunks by cosine similarity
        var scored = new List<(CodeChunk Chunk, float Score)>();

        await foreach (var chunk in store.GetAllChunksAsync(verbose, ct))
        {
            if (chunk.Embedding.Length == 0)
                continue;

            if (chunk.Embedding.Length != queryEmbedding.Length)
                continue;

            var score = CosineSimilarity(queryEmbedding, chunk.Embedding);
            scored.Add((chunk, score));
        }

        if (scored.Count == 0)
        {
            return new SearchContext(question, Array.Empty<ScoredChunk>());
        }

        var top = scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => new ScoredChunk(s.Chunk, s.Score))
            .ToList();

        return new SearchContext(question, top);
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
}