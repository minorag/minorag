using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Minorag.Cli.Indexing;

/// <summary>
/// Gitignore-like matcher for repo-relative paths (using '/' as separator).
/// Supports:
///   - '*'  : wildcard within a segment
///   - '**' : wildcard across segments
///   - '?'  : single character
///   - leading '/' for repo-root anchoring
///   - '!'  : negation (last rule wins)
/// </summary>
internal sealed class PathIgnoreMatcher
{
    private readonly List<Rule> _rules;

    private sealed record Rule(Regex Regex, bool IsNegation);

    private PathIgnoreMatcher(List<Rule> rules)
    {
        _rules = rules;
    }

    public static PathIgnoreMatcher? Create(
       IEnumerable<string> filePatterns,
       IEnumerable<string> cliPatterns)
    {
        var all = new List<string>();

        all.AddRange(filePatterns);

        all.AddRange(cliPatterns);

        if (all.Count == 0)
            return null;

        var rules = new List<Rule>();

        foreach (var raw in all)
        {
            var pattern = raw.Trim();
            if (string.IsNullOrEmpty(pattern) || pattern.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var isNegation = pattern[0] == '!';
            if (isNegation)
            {
                if (pattern.Length == 1)
                {
                    continue;
                }
                pattern = pattern[1..].TrimStart();
            }

            if (string.IsNullOrEmpty(pattern))
            {
                continue;
            }

            try
            {
                var regex = BuildRegex(pattern);
                rules.Add(new Rule(regex, isNegation));
            }
            catch (Exception ex)
            {
                // This is defensive; our builder shouldn't normally throw.
                AnsiConsole.MarkupLine(
                    "[yellow]⚠[/] Invalid ignore pattern [red]{0}[/]: [yellow]{1}[/]. Ignoring.",
                    Markup.Escape(raw),
                    Markup.Escape(ex.Message));
            }
        }

        return rules.Count == 0 ? null : new PathIgnoreMatcher(rules);
    }

    /// <summary>
    /// Returns true if the given repo-relative path should be ignored.
    /// </summary>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        if (_rules.Count == 0)
        {
            return false;
        }

        var normalized = NormalizePath(relativePath);

        if (isDirectory && !normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized += "/";
        }

        var fileName = Path.GetFileName(normalized.TrimEnd('/'));

        bool? included = null;

        foreach (var rule in _rules)
        {
            var matchFullPath = rule.Regex.IsMatch(normalized);
            var matchFileName = rule.Regex.IsMatch(fileName);

            if (!matchFullPath && !matchFileName)
                continue;

            included = rule.IsNegation;
        }

        return included is false;
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private static Regex BuildRegex(string pattern)
    {
        // Repo paths we pass in are always relative. Strip leading "/" if present.
        pattern = pattern.Replace('\\', '/');
        if (pattern.StartsWith('/'))
        {
            pattern = pattern[1..];
        }

        var regex = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (c == '*')
            {
                var isDoubleStar =
                    i + 1 < pattern.Length &&
                    pattern[i + 1] == '*';

                if (isDoubleStar)
                {
                    // '**' → match any number of segments
                    regex.Append(".*");
                    i++; // skip next '*'
                }
                else
                {
                    // '*' → match within a segment
                    regex.Append("[^/]*");
                }

                continue;
            }

            if (c == '?')
            {
                regex.Append("[^/]");
                continue;
            }

            if ("+()^$.{}[]|\\".Contains(c))
            {
                regex.Append('\\');
                regex.Append(c);
                continue;
            }

            regex.Append(c);
        }

        regex.Append("$");
        return new Regex(regex.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}