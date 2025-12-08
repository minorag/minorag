using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minorag.Cli.Configuration;
using Minorag.Cli.Store;

namespace Minorag.Cli.Hosting;

public static class HostFactory
{
    public static IHost BuildHost(string dbPath)
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
            .RegisterDatabase(dbPath);

        var host = builder.Build();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RagDbContext>();
            db.Database.Migrate();
        }

        return host;
    }
}