using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Providers;
using Minorag.Cli.Services;
using Minorag.Cli.Store;
using Spectre.Console;

namespace Minorag.Cli.Indexing;

public interface IIndexer
{
    Task IndexAsync(
        string rootPath,
        bool reindex,
        string[] excludePatterns,
        CancellationToken ct);
}

public class Indexer(
    ISqliteStore store,
    IEmbeddingProvider provider,
    IOptions<RagOptions> ragOptions) : IIndexer
{
    public const string DockerFile = "dockerfile";
    public const string Makefile = "makefile";
    public const string ReadmeFile = "readme";
    public const string LicenseFile = "license";

    private static readonly HashSet<string> ExcludedFiles = ExcludedPatterns.ExcludedFiles;
    private static readonly HashSet<string> ExcludedDirs = ExcludedPatterns.ExcludedDirs;
    private static readonly HashSet<string> BinaryExtensions = ExcludedPatterns.BinaryExtensions;
    public async Task IndexAsync(
            string rootPath,
            bool reindex,
            string[] excludePatterns,
            CancellationToken ct)
    {
        // Normalize path
        rootPath = Path.GetFullPath(rootPath);

        // ---------------------------------------------------------------------
        // Build ignore matcher: .minoragignore + CLI --exclude (CLI wins)
        // ---------------------------------------------------------------------
        var cliPatterns = ValidateCliPatterns(excludePatterns);
        var filePatterns = LoadMinoragIgnorePatterns(rootPath);
        var matcher = PathIgnoreMatcher.Create(filePatterns, cliPatterns);

        var ignoredFilesCount = 0;
        var ignoredDirsCount = 0;

        // Resolve / create repository row
        var repository = await store.GetOrCreateRepositoryAsync(rootPath, ct);

        AnsiConsole.MarkupLine(
            "[cyan]Loading existing file hashes for repo:[/] [blue]{0}[/]",
            Escape(repository.RootPath));

        var existingHashes = await store.GetFileHashesAsync(repository.Id, ct);

        // Enumerate *candidate* files, honoring:
        // - built-in ExcludedDirs
        // - .minoragignore + --exclude (matcher)
        var allFiles = EnumerateFiles(
                rootPath,
                matcher,
                onIgnoredFile: file =>
                {
                    ignoredFilesCount++;
                    AnsiConsole.MarkupLine(
                        "[grey]Skipping ignored file:[/] {0}",
                        Escape(Path.GetRelativePath(rootPath, file)));
                },
                onIgnoredDir: dir =>
                {
                    ignoredDirsCount++;
                    AnsiConsole.MarkupLine(
                        "[grey]Skipping ignored directory:[/] {0}",
                        Escape(Path.GetRelativePath(rootPath, dir)));
                })
            .Where(file =>
            {
                var fileName = Path.GetFileName(file);

                if (ExcludedFiles.Contains(fileName))
                    return false;

                var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();

                // 1. Decide if this is a special text file without extension
                var isFileWithNoExtension =
                    string.IsNullOrEmpty(ext) &&
                    IsFileWithNoExtension(fileName);

                if (isFileWithNoExtension)
                    return true;

                // 2. Normal extension-based handling
                return !string.IsNullOrEmpty(ext) && !BinaryExtensions.Contains(ext);
            })
            .ToList();

        AnsiConsole.MarkupLine(
            "[cyan]Found[/] [green]{0}[/] [cyan]candidate files to index (after built-in + ignore filtering).[/]",
            allFiles.Count);

        if (ignoredFilesCount + ignoredDirsCount > 0)
        {
            AnsiConsole.MarkupLine(
                "[cyan]Ignored[/] [yellow]{0}[/] [cyan]paths via .minoragignore / --exclude (files: {1}, dirs: {2}).[/]",
                ignoredFilesCount + ignoredDirsCount,
                ignoredFilesCount,
                ignoredDirsCount);
        }

        var indexedCount = 0;

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
                    var fileName = Path.GetFileName(file);

                    if (string.IsNullOrEmpty(ext))
                    {
                        if (fileName.Equals(DockerFile, StringComparison.OrdinalIgnoreCase))
                            ext = DockerFile;
                        else if (fileName.Equals(Makefile, StringComparison.OrdinalIgnoreCase))
                            ext = Makefile;
                        else if (fileName.Equals(LicenseFile, StringComparison.OrdinalIgnoreCase))
                            ext = "txt";
                        else if (fileName.Equals(ReadmeFile, StringComparison.OrdinalIgnoreCase))
                            ext = "md";
                    }

                    var relPath = Path.GetRelativePath(rootPath, file);

                    var content = await File.ReadAllTextAsync(file, ct);
                    var fileHash = CryptoHelper.ComputeSha256(content);

                    if (existingHashes.TryGetValue(relPath, out var oldHash) && !reindex &&
                        string.Equals(oldHash, fileHash, StringComparison.Ordinal))
                    {
                        // Unchanged
                        AnsiConsole.MarkupLine(
                            "[dim][grey]⚪ Unchanged, skipping:[/] {0}[/]",
                            Escape(relPath));
                        task.Increment(1);
                        continue;
                    }

                    if (oldHash is null)
                    {
                        // New file
                        AnsiConsole.MarkupLine(
                            "[green]➕ New file, indexing:[/] [cyan]{0}[/]",
                            Escape(relPath));
                    }
                    else if (reindex)
                    {
                        AnsiConsole.MarkupLine(
                            "[yellow]♻ Re-index applied, re-indexing:[/] [cyan]{0}[/]",
                            Escape(relPath));
                    }
                    else
                    {
                        AnsiConsole.MarkupLine(
                            "[yellow]♻ Changed file, re-indexing:[/] [cyan]{0}[/]",
                            Escape(relPath));
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
                                "[yellow]⚠️  [bold]Failed to embed[/] [cyan]{0}[/]: [red]{1}[/]",
                                Escape(chunk.Path),
                                Escape(ex.Message));
                            continue;
                        }

                        await store.InsertChunkAsync(chunk, ct);
                    }

                    indexedCount++;
                    task.Increment(1);
                }
            });

        await store.SetRepositoryLastIndexDate(repository.Id, ct);

        AnsiConsole.MarkupLine(
            "[cyan]Indexing completed.[/] [green]{0}[/] files indexed, [yellow]{1}[/] paths skipped (ignored).",
            indexedCount,
            ignoredFilesCount + ignoredDirsCount);
    }

    // ------------------------------------------------------------------------
    // .minoragignore loading
    // ------------------------------------------------------------------------

    private static IReadOnlyList<string> LoadMinoragIgnorePatterns(string repoRoot)
    {
        var ignorePath = Path.Combine(repoRoot, ".minoragignore");
        if (!File.Exists(ignorePath))
        {
            return Array.Empty<string>();
        }

        var patterns = new List<string>();
        var invalid = new List<string>();

        foreach (var raw in File.ReadAllLines(ignorePath))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            var hasOpen = line.Contains('[');
            var hasClose = line.Contains(']');

            if (hasOpen ^ hasClose)
            {
                invalid.Add(line);
                continue;
            }

            patterns.Add(line);
        }

        if (invalid.Count > 0)
        {
            AnsiConsole.MarkupLine(
                "[yellow]⚠[/] .minoragignore contains [cyan]{0}[/] invalid patterns:",
                invalid.Count);

            foreach (var rule in invalid.Take(10))
            {
                AnsiConsole.MarkupLine(
                    "    [yellow]⚠[/] Invalid ignore pattern [red]{0}[/], ignoring.",
                    Markup.Escape(rule));
            }

            if (invalid.Count > 10)
            {
                AnsiConsole.MarkupLine(
                    "    [grey]… and {0} more invalid patterns.[/]",
                    invalid.Count - 10);
            }
        }

        return patterns;
    }

    // ------------------------------------------------------------------------
    // CLI pattern validation
    // ------------------------------------------------------------------------

    private static string[] ValidateCliPatterns(string[] patterns)
    {
        if (patterns is null || patterns.Length == 0)
            return Array.Empty<string>();

        var valid = new List<string>();

        foreach (var raw in patterns)
        {
            var pattern = raw?.Trim();
            if (string.IsNullOrEmpty(pattern))
                continue;

            var hasOpen = pattern.Contains('[');
            var hasClose = pattern.Contains(']');

            if (hasOpen ^ hasClose)
            {
                throw new ArgumentException(
                    $"Invalid --exclude pattern: '{pattern}'. Unbalanced '[' / ']'.",
                    nameof(patterns));
            }

            valid.Add(pattern);
        }

        return valid.ToArray();
    }

    // ------------------------------------------------------------------------
    // Filesystem traversal with ignore support
    // ------------------------------------------------------------------------

    private static IEnumerable<string> EnumerateFiles(
        string root,
        PathIgnoreMatcher? matcher,
        Action<string>? onIgnoredFile,
        Action<string>? onIgnoredDir)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            // Skip built-in excluded directories
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name) && ExcludedDirs.Contains(name))
            {
                onIgnoredDir?.Invoke(dir);
                continue;
            }

            // Apply ignore matcher for directories (repo-relative)
            if (matcher is not null)
            {
                var relDir = Path.GetRelativePath(root, dir);
                if (!string.IsNullOrEmpty(relDir) &&
                    matcher.IsIgnored(relDir, isDirectory: true))
                {
                    onIgnoredDir?.Invoke(dir);
                    continue;
                }
            }

            foreach (var sub in Directory.GetDirectories(dir))
            {
                stack.Push(sub);
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                if (matcher is not null)
                {
                    var relFile = Path.GetRelativePath(root, file);
                    if (matcher.IsIgnored(relFile, isDirectory: false))
                    {
                        onIgnoredFile?.Invoke(file);
                        continue;
                    }
                }

                yield return file;
            }
        }
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private static bool IsFileWithNoExtension(string fileName)
    {
        return fileName.Equals(DockerFile, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(Makefile, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(LicenseFile, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(ReadmeFile, StringComparison.OrdinalIgnoreCase);
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

    private static string GuessLanguage(string ext)
    {
        // Normal extension-based handling
        return ext switch
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
            DockerFile => DockerFile,
            Makefile => "make",
            _ => "text"
        };
    }

    private static string Escape(string text)
        => text is null ? string.Empty : Markup.Escape(text);
}