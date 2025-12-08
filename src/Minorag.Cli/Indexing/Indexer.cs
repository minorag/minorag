using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Providers;
using Minorag.Cli.Store;
using Spectre.Console;

namespace Minorag.Cli.Indexing;

public interface IIndexer
{
    Task IndexAsync(string rootPath, CancellationToken ct);
}

public class Indexer(
    ISqliteStore store,
    IEmbeddingProvider provider,
    IOptions<RagOptions> ragOptions) : IIndexer
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", ".venv",
        "__pycache__", ".mypy_cache", ".pytest_cache",
        ".gradle", "build", "out", "target",
        "dist", "coverage", ".next", ".angular", ".nuxt", "storybook-static",
        "vendor", "logs", "tmp", "temp", ".cache",
        "cmake-build-debug", "cmake-build-release", "CMakeFiles"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "ico", "jar", "woff", "woff2", "dll", "exe", "pdb", "snap",
        "gif", "jpg", "jpeg", "so",

        // Images
        "bmp", "tiff", "webp", "svgz",

        // Design
        "ai", "eps", "psd", "sketch",

        // Documents
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx",

        // Audio + video
        "mp3", "wav", "ogg", "mp4", "mov", "mkv", "avi",

        // Archives
        "zip", "rar", "7z", "tar", "gz", "bz2",

        // Binary artifacts
        "class", "wasm", "sqlite", "db", "bak",

        // Fonts
        "ttf", "otf", "eot", "ttc",

        // Misc
        "lock", "bin"
    };

    public async Task IndexAsync(string rootPath, CancellationToken ct)
    {
        // Normalize path
        rootPath = Path.GetFullPath(rootPath);

        // Resolve / create repository row
        var repository = await store.GetOrCreateRepositoryAsync(rootPath, ct);

        AnsiConsole.MarkupLine(
            $"[cyan]Loading existing file hashes for repo:[/] [blue]{Escape(repository.RootPath)}[/]");

        var existingHashes = await store.GetFileHashesAsync(repository.Id, ct);

        // Precompute candidate files (non-binary)
        var allFiles = EnumerateFiles(rootPath)
            .Where(file =>
            {
                var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                return !string.IsNullOrEmpty(ext) && !BinaryExtensions.Contains(ext);
            })
            .ToList();

        AnsiConsole.MarkupLine($"[cyan]Found[/] [green]{allFiles.Count}[/] [cyan]candidate files to index.[/]");

        // Progress bar for indexing
        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[white]Indexing files[/]", maxValue: allFiles.Count);

                foreach (var file in allFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    var relPath = Path.GetRelativePath(rootPath, file);

                    var content = await File.ReadAllTextAsync(file, ct);
                    var fileHash = ComputeSha256(content);

                    if (existingHashes.TryGetValue(relPath, out var oldHash) &&
                        string.Equals(oldHash, fileHash, StringComparison.Ordinal))
                    {
                        // Unchanged
                        AnsiConsole.MarkupLine(
                            $"[dim][grey]⚪ Unchanged, skipping:[/] {Escape(relPath)}[/]");
                        task.Increment(1);
                        continue;
                    }

                    if (oldHash is null)
                    {
                        // New file
                        AnsiConsole.MarkupLine(
                            $"[green]➕ New file, indexing:[/] [cyan]{Escape(relPath)}[/]");
                    }
                    else
                    {
                        // Changed file
                        AnsiConsole.MarkupLine(
                            $"[yellow]♻ Changed file, re-indexing:[/] [cyan]{Escape(relPath)}[/]");
                    }

                    await store.DeleteChunksForFileAsync(repository.Id, relPath, ct);

                    var chunkIndex = 0;

                    foreach (var chunkContent in ChunkContent(content, maxChars: ragOptions.Value.MaxChunkSize))
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
                            AnsiConsole.MarkupLine(
                                $"[yellow]⚠️  [bold]Failed to embed[/] [cyan]{Escape(chunk.Path)}[/]:[/] [red]{Escape(ex.Message)}[/]");
                            continue;
                        }

                        await store.InsertChunkAsync(chunk, ct);
                    }

                    task.Increment(1);
                }
            });

        await store.SetRepositoryLastIndexDate(repository.Id, ct);
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
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
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

    private static string Escape(string text)
        => text is null ? string.Empty : Markup.Escape(text);
}