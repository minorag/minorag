using System.CommandLine;

namespace Minorag.Cli.Cli;

public static class CliOptions
{
    public static readonly Option<bool> DeepOption =
        new("--deep")
        {
            Description = "Use the advanced LLM model from configuration (slower but deeper reasoning)."
        };

    public static readonly Option<string[]> RepoNameOption =
        new("--repo")
        {
            Description = "Limit search to one or more repository *names* (can be repeated)."
        };

    public static readonly Option<string?> RepoNamesCsvOption =
        new("--repos")
        {
            Description = "Comma-separated list of repository names to scope the search to."
        };

    public static readonly Option<bool> AllReposOption =
        new("--all-repos")
        {
            Description = "Search across all indexed repositories (explicit opt-in)."
        };

    public static readonly Option<int?> TopKOption =
        new("--top-k")
        {
            Description = "Override default number of retrieved chunks (Top-K)."
        };

    public static readonly Option<DirectoryInfo> RepoOption =
        new("--repo")
        {
            Description = "Path to the repository to index (default: current git root)"
        };

    public static readonly Argument<string> QuestionArgument =
        new("question")
        {
            Description = "Question to ask about the indexed codebase"
        };

    public static readonly Option<FileInfo?> DbOption =
        new("--db")
        {
            Description = "Path to SQLite database file (default: ~/.minorag/index.db)"
        };

    public static readonly Option<string?> ClientOption =
        new("--client")
        {
            Description = "Client name to associate this repo with (e.g. 'Acme Corp')"
        };

    public static readonly Option<string?> ProjectOption =
        new("--project")
        {
            Description = "Project name to associate this repo with (e.g. 'Minorag')"
        };

    public static readonly Option<bool> VerboseOption =
        new("--verbose")
        {
            Description = "Print retrieved snippets (context) before the LLM answer"
        };

    public static readonly Option<bool> NoLlmOption =
        new("--no-llm")
        {
            Description = "Only show retrieved files/snippets without asking the LLM"
        };

    static CliOptions()
    {
        DbOption.Aliases.Add("-d");
        VerboseOption.Aliases.Add("-v");
        TopKOption.Aliases.Add("-k");
    }
}
