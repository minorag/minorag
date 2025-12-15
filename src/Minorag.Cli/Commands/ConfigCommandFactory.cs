using System.Collections;
using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Minorag.Cli.Cli;
using Minorag.Core.Models.Options;
using Minorag.Core.Services;

namespace Minorag.Cli.Commands;

public static class ConfigCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("config")
        {
            Description = "Configuration utilities"
        };

        var show = new Command("show")
        {
            Description = "Show effective Minorag configuration"
        };

        // Respect the shared CLI options (same style as PromptCommandFactory)
        show.Add(CliOptions.DbOption);

        show.SetAction((parseResult, cancellationToken) =>
        {
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            // Build configuration the same way as in RagDbContextFactory
            var basePath = AppContext.BaseDirectory;

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.local.json", optional: true)
                .AddEnvironmentVariables(prefix: "MINORAG_")
                .Build();

            // Bind options with fallbacks to defaults
            var ollamaSection = configuration.GetSection("Ollama");
            var ollama = ollamaSection.Get<OllamaOptions>() ?? new OllamaOptions();

            var ragSection = configuration.GetSection("Rag");
            var rag = ragSection.Get<RagOptions>() ?? new RagOptions();

            // Collect environment overrides (any MINORAG_* variables)
            const string envPrefix = "MINORAG_";
            var envOverrides = Environment.GetEnvironmentVariables()
                .Cast<DictionaryEntry>()
                .Select(e => (string)e.Key)
                .Where(k => k.StartsWith(envPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k)
                .ToArray();

            // Print effective configuration
            Console.WriteLine("Effective Minorag Configuration");
            Console.WriteLine("--------------------------------");
            Console.WriteLine($"Database path:   {dbPath}");
            Console.WriteLine($"Chat model:      {ollama.ChatModel}");
            Console.WriteLine($"Adv. chat model: {ollama.AdvancedChatModel}");
            Console.WriteLine($"Embedding model: {ollama.EmbeddingModel}");
            Console.WriteLine($"Temperature:     {ollama.Temperature}");
            Console.WriteLine($"Max chunk size:  {rag.MaxChunkSize}");
            Console.WriteLine($"Max Tokens:  {rag.MaxChunkTokens}");
            Console.WriteLine($"Max Max Overlap Tokens:  {rag.MaxChunkOverlapTokens}");
            Console.WriteLine($"Top-K:           {rag.TopK}");

            if (envOverrides.Length == 0)
            {
                Console.WriteLine("Environment overrides: (none)");
            }
            else
            {
                Console.WriteLine("Environment overrides: " +
                                  string.Join(", ", envOverrides));
            }

            // No async work right now, but SetAction expects a Task.
            return Task.CompletedTask;
        });

        cmd.Add(show);
        return cmd;
    }
}