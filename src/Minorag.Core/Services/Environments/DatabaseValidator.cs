using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Minorag.Core.Models;
using Minorag.Core.Store;

namespace Minorag.Core.Services.Environments;

public sealed class DatabaseValidator(RagDbContext db, IFileSystemHelper fs, string dbPath) : IValidator
{
    private const string OrphanedChunksLabel = "Orphaned chunks";

    public async IAsyncEnumerable<EnvironmentCheckResult> ValidateAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var (existenceResult, isExists) = CheckDatabaseExits(dbPath);

        yield return existenceResult;

        if (!isExists)
        {
            yield break;
        }

        var (connectResult, isConnectionSuccessful) = await CheckDatabaseConnection(dbPath, ct);

        yield return connectResult;

        if (!isConnectionSuccessful)
        {
            yield break;
        }

        await foreach (var result in CheckMigrations(ct).WithCancellation(ct))
        {
            yield return result;
        }

        EnvironmentCheckResult orphanedChunksResult;
        try
        {
            orphanedChunksResult = await CheckChunksWithNoEmbeddings(ct);
        }
        catch (Exception e)
        {
            orphanedChunksResult = new EnvironmentCheckResult(
                OrphanedChunksLabel,
                $"Error while fetching chunks: {e.Message}",
                EnvironmentIssueSeverity.Error);
        }

        yield return orphanedChunksResult;
    }

    private (EnvironmentCheckResult, bool) CheckDatabaseExits(string dbPath)
    {
        const string label = "Database existence.";

        if (fs.FileExists(dbPath))
        {
            var successResult = new EnvironmentCheckResult(label, $"Database located at [cyan]{dbPath}[/]", EnvironmentIssueSeverity.Success);
            return (successResult, true);
        }

        var errorResult = new EnvironmentCheckResult(label, $"[yellow]⚠[/] No index database found at [yellow]{dbPath}[/].", EnvironmentIssueSeverity.Error)
        {
            Hint = "Run [cyan]`minorag index`[/] to create one."
        };

        return (errorResult, true);
    }

    private async Task<(EnvironmentCheckResult, bool)> CheckDatabaseConnection(string dbPath, CancellationToken ct)
    {
        const string label = "Database Connection";
        if (!await db.Database.CanConnectAsync(ct))
        {
            var errorResult = new EnvironmentCheckResult(label, $"Could not connect to database at [cyan]{dbPath}[/].", EnvironmentIssueSeverity.Error);
            return (errorResult, false);
        }

        var result = new EnvironmentCheckResult(label, "SQLite connection opened via EF Core", EnvironmentIssueSeverity.Success);
        return (result, true);
    }

    private async IAsyncEnumerable<EnvironmentCheckResult> CheckMigrations([EnumeratorCancellation] CancellationToken ct)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();

        const string label = "Migrations Checks";
        if (applied.Count > 0)
        {
            yield return new EnvironmentCheckResult(
                label,
                $"Schema migration history found ([cyan]{applied.Count}[/] migrations applied)",
                EnvironmentIssueSeverity.Success);
        }
        else
        {
            yield return new EnvironmentCheckResult(
                label,
                $"No EF migrations history detected. Schema might be outdated. Run [cyan]`minorag index`[/] once to migrate.",
                EnvironmentIssueSeverity.Warning);
        }

        yield return await CheckDatabaseTables(ct);

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (pending.Count == 0)
        {
            yield return new EnvironmentCheckResult(
               label,
               $"Schema up-to-date (no pending migrations)",
               EnvironmentIssueSeverity.Success);
        }
        else
        {
            yield return new EnvironmentCheckResult(
               label,
               $"{pending.Count} pending migrations detected. Run [cyan]`minorag index`[/] to apply them.",
               EnvironmentIssueSeverity.Warning);
        }
    }

    private async Task<EnvironmentCheckResult> CheckChunksWithNoEmbeddings(CancellationToken ct)
    {
        var emptyEmbeddings = 0;

        await foreach (var c in db.Chunks
            .AsNoTracking()
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            if (c.Embedding is null || c.Embedding.Length == 0)
            {
                emptyEmbeddings++;
            }
        }

        if (emptyEmbeddings == 0)
        {
            return new EnvironmentCheckResult(
                OrphanedChunksLabel,
                "No chunks with an empty embedding found.",
                EnvironmentIssueSeverity.Success);
        }

        var warning = $"{emptyEmbeddings} chunk{(emptyEmbeddings == 1 ? "" : "s")} have an empty embedding.";
        var result = new EnvironmentCheckResult(
            OrphanedChunksLabel,
            warning,
            EnvironmentIssueSeverity.Warning)
        {
            Hint = "Consider removing them or re‑indexing with a valid embedding provider."
        };

        return result;
    }

    private async Task<EnvironmentCheckResult> CheckDatabaseTables(CancellationToken ct)
    {
        var missing = new List<string>();

        async Task ProbeTableAsync(string name, Func<Task> probe)
        {
            try
            {
                await probe();
            }
            catch
            {
                missing.Add(name);
            }
        }

        await ProbeTableAsync("clients", () => db.Clients.AsNoTracking().AnyAsync(ct));
        await ProbeTableAsync("projects", () => db.Projects.AsNoTracking().AnyAsync(ct));
        await ProbeTableAsync("repositories", () => db.Repositories.AsNoTracking().AnyAsync(ct));
        await ProbeTableAsync("chunks", () => db.Chunks.AsNoTracking().AnyAsync(ct));

        var label = "Database Tables Check";
        if (missing.Count == 0)
        {
            return new EnvironmentCheckResult(label, "Required tables present (clients, projects, repositories, chunks)", EnvironmentIssueSeverity.Success);
        }

        var missingRepos = string.Join(", ", missing);
        return new EnvironmentCheckResult(label, $"Database schema is missing tables: [yellow]{missingRepos}[/].", EnvironmentIssueSeverity.Error)
        {
            Hint = "[dim]Hint: run any Minorag CLI command (e.g. [cyan]`minorag index`[/]) to trigger EF Core migrations.[/]"
        };
    }
}