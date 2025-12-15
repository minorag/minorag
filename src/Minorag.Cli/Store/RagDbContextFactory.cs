using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Minorag.Core.Services;
using Minorag.Core.Store;

namespace Minorag.Cli.Store;

public sealed class RagDbContextFactory : IDesignTimeDbContextFactory<RagDbContext>
{
    public RagDbContext CreateDbContext(string[] args)
    {
        // Match your runtime configuration as closely as possible
        var basePath = AppContext.BaseDirectory;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables(prefix: "MINORAG_")
            .Build();

        // Same logic as you effectively use via DatabaseOptions/RagEnvironment
        var dbPath = RagEnvironment.GetDefaultDbPath();

        var optionsBuilder = new DbContextOptionsBuilder<RagDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new RagDbContext(optionsBuilder.Options);
    }
}