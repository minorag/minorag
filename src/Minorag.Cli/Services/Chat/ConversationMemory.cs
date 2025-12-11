using Minorag.Cli.Providers;

namespace Minorag.Cli.Services.Chat;

public interface IConversation
{
    /// <summary>
    /// Adds a turn to the conversation.
    /// </summary>
    void AddTurn(string question, string answer);

    /// <summary>
    /// Returns the last N turns (most recent first).
    /// </summary>
    IReadOnlyList<(string Question, string Answer)> GetRecent(int count);

    Task<float[]?> GetCombinedEmbedding(CancellationToken ct);

    void Clear();
}

public sealed class ConversationMemory(IEmbeddingProvider embeddingProvider, int maxTurns = 8) : IConversation
{
    private readonly int _maxTurns = maxTurns;
    private readonly LinkedList<(string Q, string A)> _turns = new();
    private float[]? _cachedEmbedding;

    public void Clear()
    {
        _turns.Clear();
        _cachedEmbedding = null;
    }

    public void AddTurn(string question, string answer)
    {
        _turns.AddLast((question, answer));
        while (_turns.Count > _maxTurns)
        {
            _turns.RemoveFirst();
        }

        _cachedEmbedding = null;
    }

    public IReadOnlyList<(string Question, string Answer)> GetRecent(int count)
    {
        var list = new List<(string, string)>();
        foreach (var (Q, A) in _turns.Reverse())
        {
            list.Add((Q, A));
            if (list.Count == count) break;
        }
        return list;
    }

    public async Task<float[]?> GetCombinedEmbedding(CancellationToken ct)
    {
        if (_cachedEmbedding is not null)
            return _cachedEmbedding;

        var recent = GetRecent(_maxTurns);
        if (recent.Count == 0)
            return null;

        // Embed the first turn to discover the dimensionality
        var firstEmbedding = await embeddingProvider.EmbedAsync(
            $"{recent[0].Question} {recent[0].Answer}",
            ct);

        if (firstEmbedding.Length == 0)
            return null;

        var dim = firstEmbedding.Length;
        var sum = new float[dim];
        var validCount = 0;

        // include the first embedding we already computed
        void Accumulate(float[] embedding)
        {
            if (embedding.Length != dim)
                return;

            for (var i = 0; i < dim; i++)
                sum[i] += embedding[i];

            validCount++;
        }

        Accumulate(firstEmbedding);

        for (var i = 1; i < recent.Count; i++)
        {
            var (q, a) = recent[i];
            var embedding = await embeddingProvider.EmbedAsync(
                $"{q} {a}",
                ct);

            Accumulate(embedding);
        }

        if (validCount == 0)
            return null;

        // Average
        for (var i = 0; i < dim; i++)
            sum[i] /= validCount;

        // Normalize to unit length
        var norm = MathF.Sqrt(sum.Sum(v => v * v));
        if (norm == 0f)
        {
            _cachedEmbedding = new float[dim]; // all zeros
            return _cachedEmbedding;
        }

        for (var i = 0; i < dim; i++)
            sum[i] /= norm;

        _cachedEmbedding = sum;
        return _cachedEmbedding;
    }
}