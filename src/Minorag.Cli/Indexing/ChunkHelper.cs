using System.Text;
using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Indexing;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Providers;

namespace Minorag.Cli.Indexing;

public interface IChunkHelper
{
    string GetExtension(string file);
    bool IsFileWithNoExtension(string fileName);
    string GuessLanguage(string ext);
    ChunkSpec ChooseChunkSpec(
        string relPath,
        string ext,
        string content);

    IEnumerable<string> ChunkContentByTokens(
        string content,
        int maxTokens,
        int overlapTokens,
        int hardMaxChars,
        SplitMode mode);
}

public class ChunkHelper(ITokenCounter tokenCounter, IOptions<RagOptions> options) : IChunkHelper
{
    public const string DockerFile = "dockerfile";
    public const string Makefile = "makefile";
    public const string ReadmeFile = "readme";
    public const string LicenseFile = "license";

    private readonly RagOptions _ragOptions = options.Value;

    public IEnumerable<string> ChunkContentByTokens(
     string content,
     int maxTokens,
     int overlapTokens,
     int hardMaxChars,
     SplitMode mode)
    {
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        content = content.Replace("\r\n", "\n");

        // Choose “units”
        IEnumerable<string> units = mode == SplitMode.Separators
            ? SplitBySeparators(content)
            : content.Split('\n').Select(l => l + "\n"); // keep newlines for code

        var current = new List<string>();
        var currentTokens = 0;
        var currentChars = 0;

        foreach (var unit in units)
        {
            var u = unit;
            if (string.IsNullOrEmpty(u))
                continue;

            // If one unit is too big -> aggressively split it into smaller parts
            if (tokenCounter.CountTokens(u) > maxTokens || u.Length > hardMaxChars)
            {
                if (current.Count > 0)
                {
                    yield return string.Concat(current);
                    current.Clear();
                    currentTokens = 0;
                    currentChars = 0;
                }

                foreach (var part in SplitAggressively(u, maxTokens, hardMaxChars, tokenCounter))
                    yield return part;

                continue;
            }

            // Would exceed chunk budget -> emit chunk (+ overlap)
            if ((currentTokens + tokenCounter.CountTokens(u) > maxTokens || currentChars + u.Length > hardMaxChars)
                && current.Count > 0)
            {
                yield return string.Concat(current);

                if (overlapTokens > 0)
                {
                    var overlap = TakeLastTokens(current, overlapTokens);
                    current = [.. overlap];
                    currentTokens = current.Sum(tokenCounter.CountTokens);
                    currentChars = current.Sum(x => x.Length);
                }
                else
                {
                    current.Clear();
                    currentTokens = 0;
                    currentChars = 0;
                }
            }

            current.Add(u);
            currentTokens += tokenCounter.CountTokens(u);
            currentChars += u.Length;
        }

        if (current.Count > 0)
            yield return string.Concat(current);
    }

    public string GetExtension(string file)
    {
        var fileName = Path.GetFileName(file);

        var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();

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

        return ext;
    }

    public bool IsFileWithNoExtension(string fileName)
    {
        return fileName.Equals(DockerFile, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(Makefile, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(LicenseFile, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(ReadmeFile, StringComparison.OrdinalIgnoreCase);
    }

    public string GuessLanguage(string ext)
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

    private static IEnumerable<string> SplitAggressively(
       string text,
       int maxTokens,
       int hardMaxChars,
       ITokenCounter tokenCounter)
    {
        var sb = new StringBuilder();

        foreach (var ch in text)
        {
            sb.Append(ch);

            if (sb.Length >= hardMaxChars || tokenCounter.CountTokens(sb.ToString()) >= maxTokens)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static IEnumerable<string> SplitBySeparators(string text)
    {
        // Good for .sln / JSON-ish: punctuation breaks into smaller “token-safe” chunks
        var sb = new StringBuilder();

        foreach (var ch in text)
        {
            sb.Append(ch);

            if (ch is '\n' or '\r' or ' ' or '\t' or ',' or ';' or ':' or '|' or '=' or '.' or '{' or '}' or '(' or ')' or '[' or ']' or '-')
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private Stack<string> TakeLastTokens(List<string> parts, int maxTokens)
    {
        var result = new Stack<string>();
        var tokens = 0;

        for (var i = parts.Count - 1; i >= 0; i--)
        {
            var t = tokenCounter.CountTokens(parts[i]);
            if (tokens + t > maxTokens)
                break;

            result.Push(parts[i]);
            tokens += t;
        }

        return result;
    }

    public ChunkSpec ChooseChunkSpec(
      string relPath,
      string ext,
      string content)
    {
        // Baseline from config
        var maxTokens = _ragOptions.MaxChunkTokens;
        var overlap = _ragOptions.MaxChunkOverlapTokens;
        var hardMaxChars = _ragOptions.MaxChunkSize; // keep your existing safety belt

        // Quick extension hints (NOT hardcoded sizes, just “mode”)
        var structuredByExt =
            ext is "sln" or "csproj" or "props" or "targets" or "json" or "yaml" or "yml" or "xml" or "toml";

        var relFileName = Path.GetFileName(relPath);
        var licenseLike =
            relFileName.Equals("LICENSE", StringComparison.OrdinalIgnoreCase)
            || relFileName.Equals("NOTICE", StringComparison.OrdinalIgnoreCase);

        // Shape heuristics (works even if ext lies)
        var sample = content.Length > 8000 ? content[..8000] : content;

        var punctuation = 0;
        var ws = 0;
        foreach (var ch in sample)
        {
            if (char.IsWhiteSpace(ch)) ws++;
            else if (char.IsPunctuation(ch) || char.IsSymbol(ch)) punctuation++;
        }

        var punctuationRatio = sample.Length == 0 ? 0 : (double)punctuation / sample.Length;

        // GUID-heavy (very common in .sln)
        var guidCount = CountGuids(sample);

        var tokenEstimate = tokenCounter.CountTokens(sample);
        var tokensPerChar = sample.Length == 0 ? 0 : (double)tokenEstimate / sample.Length;

        var isPunctuationHeavy =
            structuredByExt
            || guidCount >= 3
            || punctuationRatio >= 0.18
            || tokensPerChar >= 0.55;

        SplitMode mode;
        if (licenseLike)
        {
            mode = SplitMode.Lines;
            overlap = Math.Min(overlap, 16);
            maxTokens = Math.Min(maxTokens, 256);
            hardMaxChars = Math.Min(hardMaxChars, 2000);
        }
        else if (isPunctuationHeavy)
        {
            // .sln / JSON / config-like = worst-case tokenizer behavior
            mode = SplitMode.Separators;
            overlap = Math.Min(overlap, 8);
            maxTokens = Math.Min(maxTokens, 128);
            hardMaxChars = Math.Min(hardMaxChars, 1400);
        }
        else
        {
            // Normal code/text
            mode = SplitMode.Lines;
            // keep config values
        }

        // Never allow nonsense
        maxTokens = Math.Max(32, maxTokens);
        overlap = Math.Clamp(overlap, 0, Math.Max(0, maxTokens / 3));
        hardMaxChars = Math.Max(256, hardMaxChars);

        return new ChunkSpec(maxTokens, overlap, hardMaxChars, mode);
    }

    private static int CountGuids(string s)
    {
        // Avoid Regex allocations on the hot path
        // GUID pattern: 8-4-4-4-12 hex
        var count = 0;

        for (var i = 0; i + 36 <= s.Length; i++)
        {
            // fast pre-filter: must have '-' at these offsets
            if (s[i + 8] != '-' || s[i + 13] != '-' || s[i + 18] != '-' || s[i + 23] != '-')
                continue;

            // quick hex checks around
            if (!IsHexSpan(s, i, 8)) continue;
            if (!IsHexSpan(s, i + 9, 4)) continue;
            if (!IsHexSpan(s, i + 14, 4)) continue;
            if (!IsHexSpan(s, i + 19, 4)) continue;
            if (!IsHexSpan(s, i + 24, 12)) continue;

            count++;
            i += 35;
        }

        return count;

        static bool IsHexSpan(string s, int start, int len)
        {
            for (var j = 0; j < len; j++)
            {
                var c = s[start + j];
                var ok =
                    (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }
    }
}
