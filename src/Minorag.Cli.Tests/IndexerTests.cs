using Microsoft.Extensions.Options;
using Minorag.Cli.Indexing;
using Minorag.Cli.Services;
using Minorag.Cli.Tests.TestInfrastructure;
using Minorag.Core.Indexing;
using Minorag.Core.Models.Domain;
using Minorag.Core.Models.Options;
using Minorag.Core.Models.ViewModels;
using Minorag.Core.Providers;
using Minorag.Core.Services;

namespace Minorag.Cli.Tests;

public class IndexerTests
{
    [Fact]
    public async Task IndexAsync_SkipsBinaryExtensionsAndExcludedDirectories()
    {
        var root = CreateTempDir("indexer_binary_and_excluded");

        var srcDir = Directory.CreateDirectory(Path.Combine(root.FullName, "src"));
        var binDir = Directory.CreateDirectory(Path.Combine(root.FullName, "bin"));

        await File.WriteAllTextAsync(Path.Combine(srcDir.FullName, "code.cs"), "// some csharp code");
        await File.WriteAllTextAsync(Path.Combine(binDir.FullName, "ignored.cs"), "// should not be indexed");
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "image.png"), "PNG BINARY STUB");

        var store = new IndexerFakeStore();
        var indexer = GetIndexer(store, new FakeEmbeddingProvider { EmbeddingToReturn = [1f, 0f] });

        await indexer.IndexAsync(root.FullName, false, [], CancellationToken.None);

        Assert.Single(store.InsertedChunks);

        var chunk = store.InsertedChunks[0];
        Assert.NotNull(chunk.File);
        Assert.Equal("src/code.cs", NormalizePath(chunk.File.Path));
        Assert.Equal("cs", chunk.File.Extension);
        Assert.Equal("csharp", chunk.File.Language);

        Assert.DoesNotContain(store.InsertedChunks, c => NormalizePath(c.File!.Path).EndsWith("ignored.cs"));
        Assert.DoesNotContain(store.InsertedChunks, c => NormalizePath(c.File!.Path).EndsWith("image.png"));
    }

    [Fact]
    public async Task IndexAsync_UnchangedFile_SkipsDeleteAndInsert()
    {
        var root = CreateTempDir("indexer_unchanged");
        var content = "// unchanged file";
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "foo.cs"), content);

        var store = new IndexerFakeStore();
        store.ExistingHashes["foo.cs"] = CryptoHelper.ComputeSha256(content);

        var indexer = GetIndexer(store, new FakeEmbeddingProvider { EmbeddingToReturn = [1f, 0f] });

        await indexer.IndexAsync(root.FullName, false, [], CancellationToken.None);

        Assert.Empty(store.InsertedChunks);
        Assert.Empty(store.DeletedChunksForFileByRepoPath);
        Assert.Empty(store.CreatedFiles);
    }

    [Fact]
    public async Task IndexAsync_ChangedFile_DeletesOldChunksAndInsertsNewOnes()
    {
        var root = CreateTempDir("indexer_changed");
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "bar.cs"), "// v2");

        var store = new IndexerFakeStore();
        store.ExistingHashes["bar.cs"] = "OLD_HASH";

        var indexer = GetIndexer(store, new FakeEmbeddingProvider { EmbeddingToReturn = [1f, 0f] });

        await indexer.IndexAsync(root.FullName, false, [], CancellationToken.None);

        Assert.NotEmpty(store.InsertedChunks);

        Assert.All(store.InsertedChunks, c =>
        {
            Assert.NotNull(c.File);
            Assert.Equal("bar.cs", NormalizePath(c.File.Path));
        });

        Assert.Single(store.CreatedFiles);
        Assert.Equal("bar.cs", NormalizePath(store.CreatedFiles[0].Path));
    }

    [Fact]
    public async Task IndexAsync_LargeFile_IsSplitIntoMultipleChunks()
    {
        var root = CreateTempDir("indexer_large_file");
        var content = new string('a', 3900) + "\n" + new string('b', 3900);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "big.cs"), content);

        var store = new IndexerFakeStore();
        var indexer = GetIndexer(store, new FakeEmbeddingProvider { EmbeddingToReturn = [1f, 0f] });

        await indexer.IndexAsync(root.FullName, false, [], CancellationToken.None);

        Assert.True(store.InsertedChunks.Count >= 2);

        Assert.All(store.InsertedChunks, c =>
        {
            Assert.NotNull(c.File);
            Assert.Equal("big.cs", NormalizePath(c.File.Path));
        });

        var indices = store.InsertedChunks.Select(c => c.ChunkIndex).OrderBy(i => i).ToArray();
        Assert.Equal(indices, Enumerable.Range(0, indices.Length));
    }

    [Fact]
    public async Task IndexAsync_RespectsMinoragIgnoreFile()
    {
        var root = CreateTempDir("indexer_minoragignore");

        await File.WriteAllTextAsync(
            Path.Combine(root.FullName, ".minoragignore"),
            "generated/\n*.skip.cs");

        Directory.CreateDirectory(Path.Combine(root.FullName, "generated"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "src"));

        await File.WriteAllTextAsync(Path.Combine(root.FullName, "src/include.cs"), "// ok");
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "src/should.skip.cs"), "// skip");
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "generated/gen.cs"), "// skip");

        var store = new IndexerFakeStore();
        var indexer = GetIndexer(store, new FakeEmbeddingProvider { EmbeddingToReturn = [1f] });

        await indexer.IndexAsync(root.FullName, false, [], CancellationToken.None);

        Assert.Single(store.InsertedChunks);

        var chunk = store.InsertedChunks[0];
        Assert.NotNull(chunk.File);
        Assert.Equal("src/include.cs", NormalizePath(chunk.File.Path));

        Assert.DoesNotContain(store.InsertedChunks, c => NormalizePath(c.File!.Path).Contains("should.skip.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(store.InsertedChunks, c => NormalizePath(c.File!.Path).Contains("generated/gen.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndexAsync_RespectsExcludePatternsFromCli()
    {
        var root = CreateTempDir("indexer_cli_exclude");

        Directory.CreateDirectory(Path.Combine(root.FullName, "src"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "gen"));

        await File.WriteAllTextAsync(Path.Combine(root.FullName, "src/app.cs"), "// ok");
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "src/app.generated.cs"), "// skip");
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "gen/foo.cs"), "// skip");

        var store = new IndexerFakeStore();
        var indexer = GetIndexer(store, new FakeEmbeddingProvider { EmbeddingToReturn = [1f] });

        await indexer.IndexAsync(
            root.FullName,
            false,
            new[] { "gen/**", "*.generated.cs" },
            CancellationToken.None);

        Assert.Single(store.InsertedChunks);

        var chunk = store.InsertedChunks[0];
        Assert.NotNull(chunk.File);
        Assert.Equal("src/app.cs", NormalizePath(chunk.File.Path));

        Assert.DoesNotContain(store.InsertedChunks, c => NormalizePath(c.File!.Path).EndsWith("app.generated.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(store.InsertedChunks, c => NormalizePath(c.File!.Path).StartsWith("gen/", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------

    private static Indexer GetIndexer(IndexerFakeStore store, FakeEmbeddingProvider embeddingProvider)
    {
        var options = Options.Create(new RagOptions());
        var console = new MinoragConsole();
        var fs = new FileSystemHelper();
        var tokenCounter = new TokenCounter();
        var chunkHelper = new ChunkHelper(tokenCounter, options);
        var fileProvider = new RepositoryFilesProvider(fs, chunkHelper, console);

        return new Indexer(store, console, embeddingProvider, fileProvider, chunkHelper);
    }

    private static DirectoryInfo CreateTempDir(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "minorag-indexer-tests", name);
        if (Directory.Exists(root))
            Directory.Delete(root, true);

        Directory.CreateDirectory(root);
        return new DirectoryInfo(root);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed class IndexerFakeStore : BaseFakeStore
    {
        public Repository Repository { get; } = new()
        {
            Id = 1,
            Name = "test-repo",
            RootPath = "/fake/root"
        };

        public Dictionary<string, string> ExistingHashes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RepositoryFile> CreatedFiles { get; } = [];
        public List<CodeChunk> InsertedChunks { get; } = [];

        public List<(int repoId, string relativePath)> DeletedChunksForFileByRepoPath { get; } = [];
        public List<int> DeletedChunksForFileByFileId { get; } = [];

        // simple in-memory lookup: (repoId, relPath) -> file
        private readonly Dictionary<(int RepoId, string Path), RepositoryFile> _files = [];

        private int _nextFileId = 1;

        public override Task<Repository> GetOrCreateRepositoryAsync(string repoRoot, CancellationToken ct)
        {
            Repository.RootPath = repoRoot;
            return Task.FromResult(Repository);
        }

        public override Task<Dictionary<string, string>> GetFileHashesAsync(int repoId, CancellationToken ct)
            => Task.FromResult(new Dictionary<string, string>(ExistingHashes));

        public override Task CreateFile(RepositoryFile file, CancellationToken ct)
        {
            if (file.Id == 0)
                file.Id = _nextFileId++;

            if (file.RepositoryId == 0)
                file.RepositoryId = Repository.Id;

            file.Path = NormalizePath(file.Path);

            _files[(file.RepositoryId, file.Path)] = file;
            CreatedFiles.Add(file);

            return Task.CompletedTask;
        }

        public override Task<RepositoryFile?> GetFile(int repositoryId, string relPath, CancellationToken ct)
        {
            relPath = NormalizePath(relPath);
            _files.TryGetValue((repositoryId, relPath), out var file);
            return Task.FromResult(file);
        }

        public override Task DeleteChunksForFileAsync(int repoId, string relativePath, CancellationToken ct)
        {
            DeletedChunksForFileByRepoPath.Add((repoId, NormalizePath(relativePath)));
            return Task.CompletedTask;
        }

        public override Task DeleteChunksForFileAsync(int fileId, CancellationToken ct)
        {
            DeletedChunksForFileByFileId.Add(fileId);
            return Task.CompletedTask;
        }

        public override Task InsertChunkAsync(CodeChunk chunk, CancellationToken ct)
        {
            // Ensure chunk has a File object (Indexer should set it, but fake store can be resilient)
            if (chunk.File == null)
            {
                // try to resolve by file id
                if (chunk.FileId != 0)
                {
                    var match = _files.Values.FirstOrDefault(f => f.Id == chunk.FileId);
                    if (match != null)
                        chunk.File = match;
                }
            }

            InsertedChunks.Add(chunk);
            return Task.CompletedTask;
        }

        // used by some UI / view-model listing paths
        public override async IAsyncEnumerable<CodeChunkVm> GetAllChunksAsync(
            List<int>? repositoryIds = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var chunk in InsertedChunks)
            {
                ct.ThrowIfCancellationRequested();

                var file = chunk.File;
                if (file == null)
                {
                    await Task.Yield();
                    continue;
                }

                if (repositoryIds is { Count: > 0 } && !repositoryIds.Contains(file.RepositoryId))
                {
                    await Task.Yield();
                    continue;
                }

                // NOTE: adjust property names if your CodeChunkVm differs
                yield return new CodeChunkVm
                {
                    Id = chunk.Id,
                    Path = file.Path,
                    Extension = file.Extension,
                    Language = file.Language,
                    Kind = file.Kind,
                    Content = chunk.Content,
                    ChunkIndex = chunk.ChunkIndex
                };

                await Task.Yield();
            }
        }

        public override Task SetRepositoryLastIndexDate(int repoId, CancellationToken ct)
        {
            Repository.LastIndexedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }
    }
}