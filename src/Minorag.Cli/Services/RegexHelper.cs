using System.Text.RegularExpressions;

namespace Minorag.Cli.Services;

public static partial class RegexHelper
{
    private static readonly Regex HeadingsH3RegexField = HeadingsH3();
    private static readonly Regex HeadingsH2RegexField = HeadingsH2();

    private static readonly Regex HeadingsH1RegexField = HeadingsH1();

    private static readonly Regex BoldRegexField = Bold();

    private static readonly Regex ItalicRegexField = Italic();

    private static readonly Regex InlineCodeRegexField = InlineCode();

    private static readonly Regex BulletHyphenRegexField = BulletHyphen();

    private static readonly Regex BulletStarRegexField = BulletStar();
    private static readonly Regex HtmlLineBreaksField = HtmlLineBreaks();

    public static Regex HeadingsH3Regex => HeadingsH3RegexField;
    public static Regex HeadingsH2Regex => HeadingsH2RegexField;

    public static Regex HeadingsH1Regex => HeadingsH1RegexField;

    public static Regex BoldRegex => BoldRegexField;

    public static Regex ItalicRegex => ItalicRegexField;

    public static Regex InlineCodeRegex => InlineCodeRegexField;

    public static Regex BulletHyphenRegex => BulletHyphenRegexField;

    public static Regex BulletStarRegex => BulletStarRegexField;
    public static Regex HtmlLineBreaksRegex => HtmlLineBreaksField;


    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-DE")]
    private static partial Regex HtmlLineBreaks();

    [GeneratedRegex(@"^###\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HeadingsH3();
    [GeneratedRegex(@"^##\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HeadingsH2();
    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HeadingsH1();
    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Compiled)]
    private static partial Regex Bold();
    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled)]
    private static partial Regex Italic();
    [GeneratedRegex("`([^`]+)`", RegexOptions.Compiled)]
    private static partial Regex InlineCode();
    [GeneratedRegex(@"^(\s*)- ", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex BulletHyphen();
    [GeneratedRegex(@"^(\s*)\* ", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex BulletStar();
}
