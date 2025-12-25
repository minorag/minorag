using Minorag.Cli.Tests.TestInfrastructure;
using Minorag.Core.Models.Domain;
using Minorag.Core.Models.ViewModels;
using Minorag.Core.Services;

namespace Minorag.Cli.Tests;

public class SearcherTests
{
    [Fact]
    public async Task RetrieveAsync_EmptyQuestion_ThrowsArgumentException()
    {
        var store = new SearchFakeStore();
        var embedding = new FakeEmbeddingProvider();
        var llm = new FakeLlmClient();
        var searcher = new Searcher(store, embedding, llm);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            searcher.RetrieveAsync("   ", verbose: false, topK: 5, ct: CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_NoChunks_ReturnsEmptyContext()
    {
        var store = new SearchFakeStore(); // no chunks
        var embedding = new FakeEmbeddingProvider { EmbeddingToReturn = [1f, 0f, 0f] };
        var llm = new FakeLlmClient();
        var searcher = new Searcher(store, embedding, llm);

        var context = await searcher.RetrieveAsync("some question", verbose: false, topK: 5, ct: CancellationToken.None);

        Assert.Equal("some question", context.Question);
        Assert.False(context.HasResults);
        Assert.Empty(context.Chunks);
    }

    [Fact]
    public async Task RetrieveAsync_FiltersByEmbeddingLength_AndOrdersByCosineSimilarity()
    {
        var store = new SearchFakeStore();
        var embedding = new FakeEmbeddingProvider { EmbeddingToReturn = [1f, 0f, 0f] };
        var llm = new FakeLlmClient();

        var chunkSameDirection = TestChunkFactory.CreateChunk(
            id: 1L,
            embedding: [1f, 0f, 0f]);

        var chunkDiagonal = TestChunkFactory.CreateChunk(
            id: 2L,
            embedding: [1f, 1f, 0f]);

        var chunkDifferentLength = TestChunkFactory.CreateChunk(
            id: 3L,
            embedding: [1f, 0f]); // should be skipped

        store.Chunks.Add(chunkSameDirection);
        store.Chunks.Add(chunkDiagonal);
        store.Chunks.Add(chunkDifferentLength);

        var searcher = new Searcher(store, embedding, llm);

        var context = await searcher.RetrieveAsync("question", verbose: false, topK: 10, ct: CancellationToken.None);

        Assert.True(context.HasResults);
        Assert.Equal(2, context.Chunks.Count);

        Assert.Equal(1L, context.Chunks[0].Chunk.Id);
        Assert.Equal(2L, context.Chunks[1].Chunk.Id);

        Assert.True(context.Chunks[0].Score >= context.Chunks[1].Score);
    }

    [Fact]
    public async Task RetrieveAsync_RespectsTopK()
    {
        var store = new SearchFakeStore();
        var embedding = new FakeEmbeddingProvider { EmbeddingToReturn = [1f, 0f] };
        var llm = new FakeLlmClient();

        for (var i = 0; i < 5; i++)
        {
            store.Chunks.Add(TestChunkFactory.CreateChunk(
                id: i + 1L,
                embedding: [1f, i]));
        }

        var searcher = new Searcher(store, embedding, llm);

        var context = await searcher.RetrieveAsync("q", verbose: false, topK: 3, ct: CancellationToken.None);

        Assert.True(context.HasResults);
        Assert.Equal(3, context.Chunks.Count);
    }

    // --------------------------------------------------------------------
    // Test doubles
    // --------------------------------------------------------------------

    private sealed class SearchFakeStore : BaseFakeStore
    {
        public List<CodeChunk> Chunks { get; } = [];

        public override async IAsyncEnumerable<CodeChunkVm> GetAllChunksAsync(
            List<int>? repositoryIds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var chunk in Chunks)
            {
                ct.ThrowIfCancellationRequested();

                // Searcher now relies on chunk.File.RepositoryId (since repoId moved off chunk).
                if (chunk.File == null)
                {
                    throw new InvalidOperationException("Test chunk is missing File navigation. Fix TestChunkFactory or test data.");
                }

                // Respect repository filtering if Searcher provides it
                if (repositoryIds is { Count: > 0 } && !repositoryIds.Contains(chunk.File.RepositoryId))
                {
                    await Task.Yield();
                    continue;
                }

                yield return new CodeChunkVm
                {
                    Id = chunk.Id,
                    Path = chunk.File.Path,
                    Language = chunk.File.Language,
                    Kind = chunk.File.Kind,
                    Content = chunk.Content,
                    Extension = chunk.File.Extension,
                    Embedding = chunk.Embedding,
                };

                await Task.Yield();
            }
        }
    }
}