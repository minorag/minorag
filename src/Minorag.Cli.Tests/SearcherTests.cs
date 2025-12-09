using Minorag.Cli.Models;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Services;
using Minorag.Cli.Tests.TestInfrastructure;

namespace Minorag.Cli.Tests;

public class SearcherTests
{
    [Fact]
    public async Task RetrieveAsync_EmptyQuestion_ThrowsArgumentException()
    {
        // Arrange
        var store = new SearchFakeStore();
        var embedding = new FakeEmbeddingProvider();
        var llm = new FakeLlmClient();
        var searcher = new Searcher(store, embedding, llm);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            searcher.RetrieveAsync("   ", verbose: false, topK: 5, ct: CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_NoChunks_ReturnsEmptyContext()
    {
        // Arrange
        var store = new SearchFakeStore(); // no chunks
        var embedding = new FakeEmbeddingProvider
        {
            EmbeddingToReturn = [1f, 0f, 0f]
        };
        var llm = new FakeLlmClient();
        var searcher = new Searcher(store, embedding, llm);

        // Act
        var context = await searcher.RetrieveAsync("some question", verbose: false, topK: 5, ct: CancellationToken.None);

        // Assert
        Assert.Equal("some question", context.Question);
        Assert.False(context.HasResults);
        Assert.Empty(context.Chunks);
    }

    [Fact]
    public async Task RetrieveAsync_FiltersByEmbeddingLength_AndOrdersByCosineSimilarity()
    {
        // Arrange
        var store = new SearchFakeStore();
        var embedding = new FakeEmbeddingProvider
        {
            // Query embedding: along X axis
            EmbeddingToReturn = [1f, 0f, 0f]
        };
        var llm = new FakeLlmClient();

        var chunkSameDirection = TestChunkFactory.CreateChunk(
            id: 1L,
            embedding: [1f, 0f, 0f] // cos = 1.0
        );

        var chunkDiagonal = TestChunkFactory.CreateChunk(
            id: 2L,
            embedding: [1f, 1f, 0f] // cos ~ 0.707
        );

        var chunkDifferentLength = TestChunkFactory.CreateChunk(
            id: 3L,
            embedding: [1f, 0f] // should be skipped
        );

        store.Chunks.Add(chunkSameDirection);
        store.Chunks.Add(chunkDiagonal);
        store.Chunks.Add(chunkDifferentLength);

        var searcher = new Searcher(store, embedding, llm);

        // Act
        var context = await searcher.RetrieveAsync("question", verbose: false, topK: 10, ct: CancellationToken.None);

        // Assert
        Assert.True(context.HasResults);
        Assert.Equal(2, context.Chunks.Count); // the different-length one is skipped

        // Ordered by score desc → chunkSameDirection first
        Assert.Equal(1L, context.Chunks[0].Chunk.Id);
        Assert.Equal(2L, context.Chunks[1].Chunk.Id);

        Assert.True(context.Chunks[0].Score >= context.Chunks[1].Score);
    }

    [Fact]
    public async Task RetrieveAsync_RespectsTopK()
    {
        // Arrange
        var store = new SearchFakeStore();
        var embedding = new FakeEmbeddingProvider
        {
            EmbeddingToReturn = new[] { 1f, 0f }
        };
        var llm = new FakeLlmClient();

        // 5 chunks with non-zero embeddings of same length
        for (var i = 0; i < 5; i++)
        {
            store.Chunks.Add(TestChunkFactory.CreateChunk(
                id: i + 1L,
                embedding: [1f, i] // different directions, but valid
            ));
        }

        var searcher = new Searcher(store, embedding, llm);

        // Act
        var context = await searcher.RetrieveAsync("q", verbose: false, topK: 3, ct: CancellationToken.None);

        // Assert
        Assert.True(context.HasResults);
        Assert.Equal(3, context.Chunks.Count);
    }

    [Fact]
    public async Task AnswerAsync_NoResultsOrUseLlmFalse_DoesNotCallLlm_ReturnsNullAnswer()
    {
        // Arrange
        var store = new SearchFakeStore();
        var embedding = new FakeEmbeddingProvider();
        var llm = new FakeLlmClient();
        var searcher = new Searcher(store, embedding, llm);

        var emptyContext = new SearchContext("q", []);

        // Act
        var result1 = await searcher.AnswerAsync(emptyContext, useLlm: true, ct: CancellationToken.None);
        var result2 = await searcher.AnswerAsync(emptyContext, useLlm: false, ct: CancellationToken.None);

        // Assert
        Assert.Null(result1.Answer);
        Assert.Null(result2.Answer);
        Assert.False(llm.WasCalled);
    }

    [Fact]
    public async Task AnswerAsync_WithResultsAndUseLlmTrue_CallsLlmAndReturnsAnswer()
    {
        // Arrange
        var store = new SearchFakeStore();
        var embedding = new FakeEmbeddingProvider();
        var llm = new FakeLlmClient
        {
            AnswerToReturn = "This is the answer"
        };
        var searcher = new Searcher(store, embedding, llm);

        var chunk = TestChunkFactory.CreateChunk(
            id: 1L,
            embedding: [1f, 0f]
        );

        var scored = new ScoredChunk(chunk, 0.9f);
        var context = new SearchContext("What does it do?", [scored]);

        // Act
        var result = await searcher.AnswerAsync(context, useLlm: true, ct: CancellationToken.None);

        // Assert
        Assert.True(llm.WasCalled);
        Assert.Equal("What does it do?", llm.LastQuestion);
        Assert.Single(llm.LastContext!);
        Assert.Equal(chunk, llm.LastContext![0]);

        Assert.Equal("What does it do?", result.Question);
        Assert.Same(context.Chunks, result.Chunks);
        Assert.Equal("This is the answer", result.Answer);
    }

    // --------------------------------------------------------------------
    // Test doubles
    // --------------------------------------------------------------------

    private sealed class SearchFakeStore : BaseFakeStore
    {
        public List<CodeChunk> Chunks { get; } = [];

        public override async IAsyncEnumerable<CodeChunk> GetAllChunksAsync(
            bool verbose,
            List<int>? repositoryIds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var chunk in Chunks)
            {
                ct.ThrowIfCancellationRequested();
                yield return chunk;
                await Task.Yield();
            }
        }
    }
}