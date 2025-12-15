using System.Text;
using System.Text.RegularExpressions;
using Minorag.Core.Services;
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
    private const char ForwardSlash = '/';
    private const char BackwardSlash = '\\';
    private const char Asterisk = '*';

    private readonly List<Rule> _rules;

    private sealed record Rule(Regex Regex, bool IsNegation);

    private PathIgnoreMatcher(List<Rule> rules)
    {
        _rules = rules;
    }

    public static PathIgnoreMatcher? Create(
        IMinoragConsole console,
        IEnumerable<string> filePatterns,
        IEnumerable<string> cliPatterns)
    {
        var all = new List<string>();

        all.AddRange(filePatterns);
        all.AddRange(cliPatterns);

        if (all.Count == 0)
        {
            return null;
        }

        var rules = new List<Rule>();

        foreach (var raw in all)
        {
            var pattern = raw.Trim();
            if (string.IsNullOrEmpty(pattern) || pattern.StartsWith('#'))
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
                console.WriteMarkupLine(
                    "[yellow]⚠[/] Invalid ignore pattern [red]{0}[/]: [yellow]{1}[/]. Ignoring.",
                    Markup.Escape(raw),
                    Markup.Escape(ex.Message));
            }
        }

        return rules.Count == 0 ? null : new PathIgnoreMatcher(rules);
    }

    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        if (_rules.Count == 0)
            return false;

        var normalized = NormalizePath(relativePath);

        if (isDirectory && !normalized.EndsWith(ForwardSlash))
            normalized += ForwardSlash;

        var fileName = Path.GetFileName(normalized.TrimEnd(ForwardSlash));
        var fileNameDir = isDirectory ? fileName + ForwardSlash : fileName;

        bool? ignored = null; // last rule wins

        foreach (var rule in _rules)
        {
            var matchFullPath = rule.Regex.IsMatch(normalized);
            var matchFileName = rule.Regex.IsMatch(fileName);
            var matchDirName = isDirectory && rule.Regex.IsMatch(fileNameDir);

            if (!matchFullPath && !matchFileName && !matchDirName)
                continue;

            ignored = !rule.IsNegation;
        }

        return ignored == true;
    }

    private static Regex BuildRegex(string pattern)
    {
        pattern = pattern.Replace(BackwardSlash, ForwardSlash);
        if (pattern.StartsWith(ForwardSlash))
            pattern = pattern[1..];

        var regex = new StringBuilder("^");
        var length = pattern.Length;

        for (var i = 0; i < length; i++)
        {
            var c = pattern[i];

            if (c == Asterisk)
            {
                var isDoubleStar = (i + 1 < length) && pattern[i + 1] == Asterisk;

                if (isDoubleStar)
                {
                    regex.Append(".*");
                    i++;
                }
                else
                {
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
                regex.Append('\\').Append(c);
                continue;
            }

            regex.Append(c);
        }

        regex.Append('$');
        return new Regex(regex.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    private static string NormalizePath(string path) => path.Replace(BackwardSlash, ForwardSlash);
}