using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Minorag.Core.Store;
using Minorag.Core.Services;

namespace Minorag.Cli.Store;

public sealed class RagDbContextFactory : IDesignTimeDbContextFactory<RagDbContext>
{
    public RagDbContext CreateDbContext(string[] args)
    {
        var basePath = AppContext.BaseDirectory;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables(prefix: "MINORAG_")
            .Build();

        var dbPath = RagEnvironment.GetDefaultDbPath();

        var optionsBuilder = new DbContextOptionsBuilder<RagDbContext>();
        optionsBuilder.UseSqlite(
            $"Data Source={dbPath}",
            b => b.MigrationsAssembly("Minorag.Cli"));

        return new RagDbContext(optionsBuilder.Options);
    }
}