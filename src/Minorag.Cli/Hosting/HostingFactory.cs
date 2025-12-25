using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minorag.Cli.Configuration;
using Minorag.Core.Configuration;
using Minorag.Core.Store;

namespace Minorag.Cli.Hosting;

public static class HostFactory
{
    public static async Task<IHost> BuildHost(string dbPath, CancellationToken ct)
    {
        var appBase = AppContext.BaseDirectory;
        var settings = new HostApplicationBuilderSettings
        {
            ContentRootPath = appBase,
            ApplicationName = "Minorag.Cli"
        };

        var builder = Host.CreateApplicationBuilder(settings);

        builder.Configuration
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.local.json", optional: true)
                .AddEnvironmentVariables(prefix: "MINORAG_");

        builder.Services.SetupConfiguration(builder.Configuration);
        builder.ConfigureLogging();
        builder.Services
            .RegisterServices()
            .RegisterDatabase(dbPath)
            .RegisterChatDependencies();

        var host = builder.Build();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RagDbContext>();
            var applied = await db.Database.GetAppliedMigrationsAsync(ct);
            var pending = await db.Database.GetPendingMigrationsAsync(ct);
            await db.Database.MigrateAsync(ct);
        }

        return host;
    }
}