using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Minorag.Core.Store;

namespace Minorag.Cli.Tests.TestInfrastructure;

public static class SqliteTestContextFactory
{
    public static RagDbContext CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseSqlite(conn)
            .Options;

        var ctx = new RagDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }
}