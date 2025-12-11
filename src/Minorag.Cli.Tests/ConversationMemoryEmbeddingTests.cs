using Minorag.Cli.Providers;
using Minorag.Cli.Services.Chat;

namespace Minorag.Cli.Tests;

public class ConversationMemoryEmbeddingTests
{
    private sealed class TestEmbeddingProvider : IEmbeddingProvider
    {
        private readonly Queue<float[]> _queue = new();
        public int CallCount { get; private set; }
        public List<string> Inputs { get; } = [];

        public void Enqueue(params float[][] embeddings)
        {
            foreach (var e in embeddings)
            {
                _queue.Enqueue(e);
            }
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            CallCount++;
            Inputs.Add(text);

            if (_queue.Count > 0)
            {
                return Task.FromResult(_queue.Dequeue());
            }

            // Default: empty embedding
            return Task.FromResult(Array.Empty<float>());
        }
    }

    [Fact]
    public async Task GetCombinedEmbedding_NoTurns_ReturnsNull_AndDoesNotCallProvider()
    {
        var provider = new TestEmbeddingProvider();
        var memory = new ConversationMemory(provider, maxTurns: 8);

        var result = await memory.GetCombinedEmbedding(CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task GetCombinedEmbedding_FirstEmbeddingEmpty_ReturnsNull()
    {
        var provider = new TestEmbeddingProvider();
        provider.Enqueue([]);

        var memory = new ConversationMemory(provider, maxTurns: 8);
        memory.AddTurn("q1", "a1");

        var result = await memory.GetCombinedEmbedding(CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task GetCombinedEmbedding_AveragesAndNormalizesAcrossTurns()
    {
        var provider = new TestEmbeddingProvider();

        provider.Enqueue(
            [1f, 0f],
            [0f, 1f],
            [1f, 1f]);

        var memory = new ConversationMemory(provider, maxTurns: 8);
        memory.AddTurn("q1", "a1");
        memory.AddTurn("q2", "a2");
        memory.AddTurn("q3", "a3");

        var result = await memory.GetCombinedEmbedding(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Length);

        var norm = MathF.Sqrt(result[0] * result[0] + result[1] * result[1]);
        Assert.InRange(norm, 0.9999f, 1.0001f);

        Assert.InRange(MathF.Abs(result[0] - result[1]), 0f, 1e-4f);
    }

    [Fact]
    public async Task GetCombinedEmbedding_IgnoresMismatchedDimensions()
    {
        var provider = new TestEmbeddingProvider();
        provider.Enqueue(
            [1f, 0f],
            [1f, 2f, 3f],
            [0f, 1f]);

        var memory = new ConversationMemory(provider, maxTurns: 8);
        memory.AddTurn("q1", "a1");
        memory.AddTurn("q2", "a2");
        memory.AddTurn("q3", "a3");

        var result = await memory.GetCombinedEmbedding(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Length);

        var norm = MathF.Sqrt(result[0] * result[0] + result[1] * result[1]);
        Assert.InRange(norm, 0.9999f, 1.0001f);
        Assert.InRange(MathF.Abs(result[0] - result[1]), 0f, 1e-4f);

        Assert.Equal(3, provider.CallCount);
    }

    [Fact]
    public async Task GetCombinedEmbedding_AllZeroEmbeddings_ReturnsZeroVector()
    {
        var provider = new TestEmbeddingProvider();
        provider.Enqueue(
            [0f, 0f],
            [0f, 0f]);

        var memory = new ConversationMemory(provider, maxTurns: 8);
        memory.AddTurn("q1", "a1");
        memory.AddTurn("q2", "a2");

        var result = await memory.GetCombinedEmbedding(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Length);
        Assert.All(result, v => Assert.Equal(0f, v));
    }

    [Fact]
    public async Task GetCombinedEmbedding_UsesCachedResult_WhenConversationUnchanged()
    {
        var provider = new TestEmbeddingProvider();
        provider.Enqueue([1f, 0f]);

        var memory = new ConversationMemory(provider, maxTurns: 8);
        memory.AddTurn("q1", "a1");

        var first = await memory.GetCombinedEmbedding(CancellationToken.None);
        var second = await memory.GetCombinedEmbedding(CancellationToken.None);

        Assert.Equal(1, provider.CallCount);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetCombinedEmbedding_Recomputes_WhenNewTurnAdded()
    {
        var provider = new TestEmbeddingProvider();
        provider.Enqueue(
            [1f, 0f],
            [0f, 1f]
        );

        var memory = new ConversationMemory(provider, maxTurns: 8);
        memory.AddTurn("q1", "a1");

        var before = await memory.GetCombinedEmbedding(CancellationToken.None);
        var callCountBefore = provider.CallCount;
        Assert.Equal(1, callCountBefore);

        memory.AddTurn("q2", "a2");

        var after = await memory.GetCombinedEmbedding(CancellationToken.None);

        // Just check that it recomputed (called provider again)
        Assert.True(provider.CallCount > callCountBefore);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task GetCombinedEmbedding_CacheClearedByClear()
    {
        var provider = new TestEmbeddingProvider();
        provider.Enqueue([1f, 0f]);

        var memory = new ConversationMemory(provider, maxTurns: 8);
        memory.AddTurn("q1", "a1");

        var first = await memory.GetCombinedEmbedding(CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(1, provider.CallCount);

        memory.Clear();

        var afterClear = await memory.GetCombinedEmbedding(CancellationToken.None);

        Assert.Null(afterClear);
        Assert.Equal(1, provider.CallCount);
    }
}