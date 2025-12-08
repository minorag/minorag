using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Minorag.Cli.Indexing;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Providers;
using Minorag.Cli.Store;

namespace Minorag.Cli.Tests;

public class IndexerTests
{
    [Fact]
    public async Task IndexAsync_SkipsBinaryExtensionsAndExcludedDirectories()
    {
        // Arrange
        var root = CreateTempDir("indexer_binary_and_excluded");

        // /root/src/code.cs         -> should be indexed
        // /root/bin/ignored.cs      -> excluded dir, should NOT be indexed
        // /root/image.png           -> binary extension, should NOT be indexed

        var srcDir = Directory.CreateDirectory(Path.Combine(root.FullName, "src"));
        var binDir = Directory.CreateDirectory(Path.Combine(root.FullName, "bin"));

        var codeFile = Path.Combine(srcDir.FullName, "code.cs");
        var ignoredFile = Path.Combine(binDir.FullName, "ignored.cs");
        var imageFile = Path.Combine(root.FullName, "image.png");

        await File.WriteAllTextAsync(codeFile, "// some csharp code");
        await File.WriteAllTextAsync(ignoredFile, "// should not be indexed");
        await File.WriteAllTextAsync(imageFile, "PNG BINARY STUB");

        var store = new FakeStore();
        var embeddingProvider = new FakeEmbeddingProvider
        {
            EmbeddingToReturn = [1f, 0f]
        };

        var ragOptions = Options.Create(new RagOptions());
        var indexer = new Indexer(store, embeddingProvider, ragOptions);

        // Act
        await indexer.IndexAsync(root.FullName, CancellationToken.None);

        // Assert
        Assert.Single(store.InsertedChunks);

        var chunk = store.InsertedChunks[0];
        Assert.Equal(NormalizePath(Path.Combine("src", "code.cs")), NormalizePath(chunk.Path));
        Assert.Equal("cs", chunk.Extension);
        Assert.Equal("csharp", chunk.Language);

        // Ensure that no other files got indexed
        Assert.DoesNotContain(store.InsertedChunks, c => NormalizePath(c.Path).EndsWith("ignored.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(store.InsertedChunks, c => NormalizePath(c.Path).EndsWith("image.png", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndexAsync_UnchangedFile_SkipsDeleteAndInsert()
    {
        // Arrange
        var root = CreateTempDir("indexer_unchanged");
        var filePath = Path.Combine(root.FullName, "foo.cs");
        var content = "// unchanged file";
        await File.WriteAllTextAsync(filePath, content);

        var relPath = "foo.cs";
        var hash = ComputeSha256(content); // must match Indexer.ComputeSha256

        var store = new FakeStore();
        store.ExistingHashes[NormalizePath(relPath)] = hash;

        var embeddingProvider = new FakeEmbeddingProvider
        {
            EmbeddingToReturn = [1f, 0f]
        };

        var ragOptions = Options.Create(new RagOptions());
        var indexer = new Indexer(store, embeddingProvider, ragOptions);

        // Act
        await indexer.IndexAsync(root.FullName, CancellationToken.None);

        // Assert
        Assert.Empty(store.InsertedChunks);
        Assert.Empty(store.DeletedChunksForFile);
    }

    [Fact]
    public async Task IndexAsync_ChangedFile_DeletesOldChunksAndInsertsNewOnes()
    {
        // Arrange
        var root = CreateTempDir("indexer_changed");
        var filePath = Path.Combine(root.FullName, "bar.cs");
        var content = "// v2 of file";
        await File.WriteAllTextAsync(filePath, content);

        var relPath = "bar.cs";

        var store = new FakeStore();
        // Old hash different from new → should re-index
        store.ExistingHashes[NormalizePath(relPath)] = "OLD_HASH";

        var embeddingProvider = new FakeEmbeddingProvider
        {
            EmbeddingToReturn = [1f, 0f]
        };

        var ragOptions = Options.Create(new RagOptions());
        var indexer = new Indexer(store, embeddingProvider, ragOptions);

        // Act
        await indexer.IndexAsync(root.FullName, CancellationToken.None);

        // Assert
        Assert.Single(store.DeletedChunksForFile);
        var delete = store.DeletedChunksForFile[0];

        Assert.Equal(store.Repository.Id, delete.repoId);
        Assert.Equal(NormalizePath(relPath), NormalizePath(delete.relativePath));

        Assert.NotEmpty(store.InsertedChunks);
        Assert.All(store.InsertedChunks, c =>
            Assert.Equal(NormalizePath(relPath), NormalizePath(c.Path)));
    }

    [Fact]
    public async Task IndexAsync_LargeFile_IsSplitIntoMultipleChunks()
    {
        // Arrange
        var root = CreateTempDir("indexer_large_file");
        var filePath = Path.Combine(root.FullName, "big.cs");

        // Two ~3900-char lines -> with default MaxChunkSize (currently 2000),
        // this will result in multiple chunks.
        var line1 = new string('a', 3900);
        var line2 = new string('b', 3900);
        var content = line1 + "\n" + line2;
        await File.WriteAllTextAsync(filePath, content);

        var store = new FakeStore();
        var embeddingProvider = new FakeEmbeddingProvider
        {
            EmbeddingToReturn = [1f, 0f, 0f]
        };

        var ragOptions = Options.Create(new RagOptions());
        var indexer = new Indexer(store, embeddingProvider, ragOptions);

        // Act
        await indexer.IndexAsync(root.FullName, CancellationToken.None);

        // Assert
        Assert.True(store.InsertedChunks.Count >= 2);
        Assert.All(store.InsertedChunks, c =>
            Assert.Equal(NormalizePath("big.cs"), NormalizePath(c.Path)));

        // Ensure chunk indices are sequential: 0..N-1
        var indices = store.InsertedChunks
            .Select(c => c.ChunkIndex)
            .OrderBy(i => i)
            .ToArray();

        Assert.Equal(indices, Enumerable.Range(0, indices.Length).ToArray());
    }

    // --------------------------------------------------------------------
    // Local helpers (you can move these into TestInfrastructure later)
    // --------------------------------------------------------------------

    private static DirectoryInfo CreateTempDir(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "minorag-indexer-tests", name);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);

        Directory.CreateDirectory(root);
        return new DirectoryInfo(root);
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    // Same SHA-256 logic as Minorag.Cli/Indexing/Indexer.cs
    private static string ComputeSha256(string content)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = sha.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes); // uppercase hex
    }

    // --------------------------------------------------------------------
    // Test doubles for ISqliteStore and IEmbeddingProvider
    // --------------------------------------------------------------------

    private sealed class FakeStore : ISqliteStore
    {
        public Repository Repository { get; } = new()
        {
            Id = 1,
            Name = "test-repo",
            RootPath = "/fake/root"
        };

        // relPath -> hash
        public Dictionary<string, string> ExistingHashes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<CodeChunk> InsertedChunks { get; } = [];
        public List<(int repoId, string relativePath)> DeletedChunksForFile { get; } = [];

        public Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct)
        {
            Repository.RootPath = repoRoot;
            return Task.FromResult(Repository);
        }

        public Task<Dictionary<string, string>> GetFileHashesAsync(int repoId, CancellationToken ct)
        {
            return Task.FromResult(new Dictionary<string, string>(ExistingHashes));
        }

        public Task DeleteChunksForFileAsync(int repoId, string relativePath, CancellationToken ct)
        {
            DeletedChunksForFile.Add((repoId, relativePath));
            return Task.CompletedTask;
        }

        public Task InsertChunkAsync(CodeChunk chunk, CancellationToken ct)
        {
            InsertedChunks.Add(chunk);
            return Task.CompletedTask;
        }

        // Not used by Indexer, but required by ISqliteStore
        public async IAsyncEnumerable<CodeChunk> GetAllChunksAsync(
            bool verbose,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var chunk in InsertedChunks)
            {
                ct.ThrowIfCancellationRequested();
                yield return chunk;
                await Task.Yield();
            }
        }

        public Task SetRepositoryLastIndexDate(int repoId, CancellationToken ct)
        {
            Repository.LastIndexedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public float[] EmbeddingToReturn { get; set; } = [];
        public string? LastText { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            LastText = text;
            return Task.FromResult(EmbeddingToReturn);
        }
    }
}