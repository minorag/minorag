using System.CommandLine;

namespace Minorag.Cli.Commands;

public static class RootCommandFactory
{
    public static RootCommand CreateRootCommand()
    {
        var root = new RootCommand("Minorag CLI")
        {
            Description = """
                          Command-line utilities for indexing and querying codebases using RAG.

                          Typical usage:
                            # Index multiple repos into one global DB
                            cd ~/dev/backend  && minorag index
                            cd ~/dev/frontend && minorag index

                            # Ask questions across all indexed repos
                            minorag ask "where is the code responsible for fetching users?"

                          Options:
                            --db, -d      Override default SQLite DB (default: ~/.minorag/index.db)
                            --verbose,-v  Print retrieved snippets
                            --no-llm      Only show retrieved files/snippets, no LLM call
                          """
        };

        root.Add(DbPathCommandFactory.Create());
        root.Add(IndexCommandFactory.Create());
        root.Add(AskCommandFactory.Create());
        root.Add(PromptCommandFactory.Create());
        root.Add(ConfigCommandFactory.Create());

        return root;
    }
}