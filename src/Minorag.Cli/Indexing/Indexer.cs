using System.Security.Cryptography;
using System.Text;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Providers;
using Minorag.Cli.Store;

namespace Minorag.Cli.Indexing;

public interface IIndexer
{
    Task IndexAsync(string rootPath, CancellationToken ct);
}

public class Indexer(ISqliteStore store, IEmbeddingProvider provider) : IIndexer
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "ico", "jar", "woff", "woff2", "dll", "exe", "pdb", "snap", "gif", "jpg", "jpeg"
    };

    public async Task IndexAsync(string rootPath, CancellationToken ct)
    {
        // Normalize path
        rootPath = Path.GetFullPath(rootPath);

        // Resolve / create repository row
        var repository = await store.GetOrCreateRepositoryAsync(rootPath, ct);

        Console.WriteLine($"Loading existing file hashes for repo: {repository.RootPath}");
        var existingHashes = await store.GetFileHashesAsync(repository.Id, ct);

        foreach (var file in EnumerateFiles(rootPath))
        {
            ct.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || BinaryExtensions.Contains(ext))
            {
                continue;
            }

            var relPath = Path.GetRelativePath(rootPath, file);

            var content = await File.ReadAllTextAsync(file, ct);
            var fileHash = ComputeSha256(content);

            if (existingHashes.TryGetValue(relPath, out var oldHash) &&
                string.Equals(oldHash, fileHash, StringComparison.Ordinal))
            {
                Console.WriteLine($"Unchanged, skipping: {relPath}");
                continue;
            }

            Console.WriteLine(oldHash is null
                ? $"New file, indexing: {relPath}"
                : $"Changed file, re-indexing: {relPath}");

            // Remove old chunks for this file+repo
            await store.DeleteChunksForFileAsync(repository.Id, relPath, ct);

            var chunkIndex = 0;

            foreach (var chunkContent in ChunkContent(content, maxChars: 4000))
            {
                ct.ThrowIfCancellationRequested();

                var chunk = new CodeChunk
                {
                    RepositoryId = repository.Id,
                    Path = relPath,
                    Extension = ext,
                    Language = GuessLanguage(ext),
                    Kind = "file",
                    SymbolName = null,
                    Content = chunkContent,
                    FileHash = fileHash,
                    ChunkIndex = chunkIndex++
                };

                try
                {
                    chunk.Embedding = await provider.EmbedAsync(chunk.Content, ct);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[WARN] Failed to embed {chunk.Path}: {ex.Message}");
                    Console.ResetColor();
                    continue;
                }

                await store.InsertChunkAsync(chunk, ct);
            }
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (ExcludedDirs.Contains(name))
                    continue;

                stack.Push(sub);
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> ChunkContent(string content, int maxChars)
    {
        if (content.Length <= maxChars)
        {
            yield return content;
            yield break;
        }

        var lines = content.Split('\n');
        var current = new List<string>();
        var currentLen = 0;

        foreach (var line in lines)
        {
            var len = line.Length + 1;
            if (currentLen + len > maxChars && current.Count > 0)
            {
                yield return string.Join('\n', current);
                current.Clear();
                currentLen = 0;
            }

            current.Add(line);
            currentLen += len;
        }

        if (current.Count > 0)
        {
            yield return string.Join('\n', current);
        }
    }

    private static string ComputeSha256(string content)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = sha.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes); // uppercase hex
    }

    private static string GuessLanguage(string ext) => ext switch
    {
        "cs" or "csproj" or "sln" or "props" or "targets" or "ruleset" => "csharp",
        "js" or "mjs" => "javascript",
        "ts" => "typescript",
        "py" => "python",
        "go" => "go",
        "html" => "html",
        "css" => "css",
        "tf" or "hcl" => "terraform",
        "yaml" or "yml" => "yaml",
        "json" => "json",
        "md" => "markdown",
        "toml" => "toml",
        "sh" or "bat" => "shell",
        _ => "text"
    };
}