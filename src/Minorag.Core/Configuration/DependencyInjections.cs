using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Minorag.Core.Services;
using Minorag.Core.Indexing;
using Minorag.Core.Services.Environments;
using Minorag.Core.Providers;
using Minorag.Core.Models.Options;
using Minorag.Core.Store;

namespace Minorag.Core.Configuration;

public static class DependencyInjections
{
    public static IServiceCollection SetupConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.Configure<RagOptions>(configuration.GetSection("Rag"));
        return services;
    }

    public static IServiceCollection RegisterServices(this IServiceCollection services)
    {
        services.AddScoped<ISqliteStore, SqliteStore>();

        services.AddHttpClient<IOllamaClient, OllamaClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            var host = string.IsNullOrWhiteSpace(options.Host)
                ? "http://127.0.0.1:11434"
                : options.Host;

            client.BaseAddress = new Uri(host);
        });

        services.AddScoped<ILlmClient, OllamaChatClient>();
        services.AddScoped<IEmbeddingProvider, OllamaEmbeddingProvider>();
        services.AddSingleton<IPromptBuilder, RichPromptBuilder>();
        services.AddScoped<ISearcher, Searcher>();
        services.AddScoped<ScopeResolver>();

        services.AddScoped<IEnvironmentDoctor, EnvironmentDoctor>();
        services.AddScoped<IEnvironmentHelper, EnvironmentHelper>();
        services.AddScoped<ConfigValidatorFactory>();
        services.AddScoped<DatabaseValidatorFactory>();
        services.AddScoped<IgnoreRulesValidatorFactory>();
        services.AddScoped<OllamaValidator>();


        services.AddSingleton<IPromptFormatter, MarkdownPromptFormatter>();
        services.AddSingleton<IFileSystemHelper, FileSystemHelper>();
        services.AddSingleton<ITokenCounter, TokenCounter>();
        services.AddSingleton<IChunkHelper, ChunkHelper>();

        services.AddScoped<IIndexPruner, IndexPruner>();

        return services;
    }

    public static IServiceCollection RegisterDatabase(this IServiceCollection services, string dbPath)
    {
        services.AddDbContext<RagDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}", b => b.MigrationsAssembly("Minorag.Cli"));
        });

        return services;
    }
}