using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minorag.Cli.Cli;
using Minorag.Cli.Indexing;
using Minorag.Cli.Services;
using Minorag.Core.Indexing;
using Minorag.Core.Services;

namespace Minorag.Cli.Configuration;

public static class CliDependencyInjection
{
    public static IServiceCollection RegisterChatDependencies(this IServiceCollection services)
    {
        services.AddScoped<IIndexer, Indexer>();
        services.AddScoped<IMinoragConsole, MinoragConsole>();
        services.AddSingleton<IConsoleSearchPresenter, ConsoleSearchPresenter>();

        services.AddSingleton<IConsoleInputProvider, ConsoleInputProvider>();


        services.AddScoped<IIndexScopeService, IndexScopeService>();
        services.AddSingleton<IRepositoryFilesProvider, RepositoryFilesProvider>();

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
