using Minorag.Core.Indexing;
using Minorag.Core.Models.Indexing;
using Minorag.Core.Services;

namespace Minorag.Cli.Indexing;

public interface IRepositoryFilesProvider
{
    Task<FileContent> ReadFileAsync(string path, CancellationToken ct);
    Task<(List<string>, int, int)> GetAllFiles(string rootPath, string[] excludePatterns, CancellationToken ct);
}

public class RepositoryFilesProvider(IFileSystemHelper fs, IChunkHelper chunkHelper, IMinoragConsole console) : IRepositoryFilesProvider
{
    private static readonly HashSet<string> ExcludedFiles = ExcludedPatterns.ExcludedFiles;
    private static readonly HashSet<string> ExcludedDirs = ExcludedPatterns.ExcludedDirs;
    private static readonly HashSet<string> BinaryExtensions = ExcludedPatterns.BinaryExtensions;

    public async Task<FileContent> ReadFileAsync(string path, CancellationToken ct)
    {
        var content = await fs.ReadAllTextAsync(path, ct);
        var fileHash = CryptoHelper.ComputeSha256(content);

        return new FileContent(content, fileHash);
    }

    public async Task<(List<string>, int, int)> GetAllFiles(string rootPath, string[] excludePatterns, CancellationToken ct)
    {
        var filePatterns = await LoadMinoragIgnorePatterns(rootPath, ct);
        var cliPatterns = ValidateCliPatterns(excludePatterns);
        var matcher = PathIgnoreMatcher.Create(console, filePatterns, cliPatterns);

        var ignoredFilesCount = 0;
        var ignoredDirsCount = 0;
        // Enumerate *candidate* files, honoring:
        // - built-in ExcludedDirs
        // - .minoragignore + --exclude (matcher)
        var allFiles = EnumerateFiles(
                rootPath,
                matcher,
                onIgnoredFile: file =>
                {
                    ignoredFilesCount++;
                    console.WriteMarkupLine(
                        "[grey]Skipping ignored file:[/] {0}",
                      console.EscapeMarkup(Path.GetRelativePath(rootPath, file)));
                },
                onIgnoredDir: dir =>
                {
                    ignoredDirsCount++;
                    console.WriteMarkupLine(
                        "[grey]Skipping ignored directory:[/] {0}",
                        console.EscapeMarkup(Path.GetRelativePath(rootPath, dir)));
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
                   chunkHelper.IsFileWithNoExtension(fileName);

                if (isFileWithNoExtension)
                    return true;

                // 2. Normal extension-based handling
                return !string.IsNullOrEmpty(ext) && !BinaryExtensions.Contains(ext);
            })
            .ToList();
        return (allFiles, ignoredFilesCount, ignoredDirsCount);
    }

    // ------------------------------------------------------------------------
    // Filesystem traversal with ignore support
    // ------------------------------------------------------------------------

    private IEnumerable<string> EnumerateFiles(
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

            foreach (var sub in fs.GetDirectories(dir))
            {
                stack.Push(sub);
            }

            foreach (var file in fs.GetFiles(dir))
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

    private async Task<List<string>> LoadMinoragIgnorePatterns(string repoRoot, CancellationToken ct)
    {
        var ignorePath = Path.Combine(repoRoot, ".minoragignore");
        if (!fs.FileExists(ignorePath))
        {
            return [];
        }

        var patterns = new List<string>();
        var invalid = new List<string>();

        foreach (var raw in await fs.ReadAllLinesAsync(ignorePath, ct))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

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
            console.WriteMarkupLine(
                "[yellow]⚠[/] .minoragignore contains [cyan]{0}[/] invalid patterns:",
                invalid.Count);

            foreach (var rule in invalid.Take(10))
            {
                console.WriteMarkupLine(
                    "    [yellow]⚠[/] Invalid ignore pattern [red]{0}[/], ignoring.",
                    console.EscapeMarkup(rule));
            }

            if (invalid.Count > 10)
            {
                console.WriteMarkupLine(
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
        {
            return [];
        }

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

        return [.. valid];
    }
}
