using Minorag.Cli.Indexing;
using Minorag.Cli.Providers;
using Minorag.Cli.Services;
using Minorag.Cli.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Options;

namespace Minorag.Cli.Configuration;

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
        services.AddScoped<IIndexer, Indexer>();
        services.AddScoped<IIndexer, Indexer>();
        services.AddScoped<ISearcher, Searcher>();
        services.AddScoped<ScopeResolver>();

        services.AddSingleton<IPromptFormatter, MarkdownPromptFormatter>();
        services.AddSingleton<IConsoleSearchPresenter, ConsoleSearchPresenter>();
        services.AddScoped<IEnvironmentDoctor, EnvironmentDoctor>();
        services.AddScoped<IIndexScopeService, IndexScopeService>();

        return services;
    }

    public static IServiceCollection RegisterDatabase(this IServiceCollection services, string dbPath)
    {
        services.AddDbContext<RagDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}");
        });

        return services;
    }

    public static void ConfigureLogging(this HostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Logging.AddFilter("System.Net.Http.HttpClient.IEmbeddingProvider", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient.ILlmClient", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Model.Validation", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Migrations", LogLevel.Warning);
    }
}