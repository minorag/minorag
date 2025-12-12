using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Minorag.Cli.Indexing;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Store;
using Spectre.Console;

namespace Minorag.Cli.Services;

public interface IEnvironmentDoctor
{
    Task RunAsync(string dbPath, string workingDirectory, CancellationToken ct);
}

public sealed class EnvironmentDoctor(
    RagDbContext db,
    IFileSystemHelper fs,
    IConfiguration configuration,
    IOptions<OllamaOptions> ollamaOptions,
    IHttpClientFactory httpClientFactory,
    IIndexPruner indexPruner,
    IMinoragConsole console) : IEnvironmentDoctor
{
    private const string Tilde = "~";
    private readonly OllamaOptions ollamaOpts = ollamaOptions.Value;

    public async Task RunAsync(string dbPath, string workingDirectory, CancellationToken ct)
    {
        var configuredDbPath = configuration.GetSection("Database")["Path"];

        await RunDatabaseChecksAsync(dbPath, configuredDbPath, ct);
        await RunIndexHealthChecksAsync(ct);
        await RunIgnoreRulesChecks(workingDirectory, ct);
        await RunOllamaChecksAsync(ct);
        RunConfigChecks(dbPath, configuredDbPath);
    }

    // ------------------------------------------------------------------------
    // Database & Schema
    // ------------------------------------------------------------------------

    private async Task RunDatabaseChecksAsync(
        string dbPath,
        string? configuredDbPath,
        CancellationToken ct)
    {
        console.WriteMarkupLine("[bold]Database & Schema[/]");
        bool dbExists = CheckDatabaseExits(dbPath);
        if (!dbExists)
        {
            return;
        }

        CheckDatabasePath(dbPath, configuredDbPath);

        try
        {
            bool canConnect = await CheckDatabaseConnection(dbPath, ct);
            if (!canConnect)
            {
                return;
            }

            console.WriteMarkupLine("[green]✔[/] SQLite connection opened via EF Core");

            await CheckChunksWithNoEmbeddings(ct);

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

            if (missing.Count == 0)
            {
                console.WriteMarkupLine(
                    "[green]✔[/] Required tables present (clients, projects, repositories, chunks)");
            }
            else
            {
                console.WriteMarkupLine(
                    "[red]✖[/] Database schema is missing tables: [yellow]{0}[/].",
                    string.Join(", ", missing));
                console.WriteMarkupLine(
                    "    [dim]Hint: run any Minorag CLI command (e.g. [cyan]`minorag index`[/]) " +
                    "to trigger EF Core migrations.[/]");
            }

            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            if (applied.Count > 0)
            {
                console.WriteMarkupLine(
                    "[green]✔[/] Schema migration history found ([cyan]{0}[/] migrations applied).",
                    applied.Count);
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] No EF migrations history detected. " +
                    "Schema might be outdated. Run [cyan]`minorag index`[/] once to migrate.");
            }

            if (pending.Count == 0)
            {
                console.WriteMarkupLine("[green]✔[/] Schema up-to-date (no pending migrations)");
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] {0} pending migrations detected. Run [cyan]`minorag index`[/] to apply them.",
                    pending.Count);
            }
        }
        catch (Exception ex)
        {
            console.WriteMarkupLine(
                "[red]✖[/] Error while checking database: [yellow]{0}[/]",
                Markup.Escape(ex.Message));
            console.WriteMarkupLine(
                "    [dim]Recovery hint: backup then remove index.db and re-run [cyan]`minorag index`[/] to rebuild.[/]");
        }

        console.WriteLine();
    }

    private async Task CheckChunksWithNoEmbeddings(CancellationToken ct)
    {
        var emptyEmbeddings = 0;

        await foreach (var c in db.Chunks
            .AsNoTracking()
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            if (c.Embedding is null || c.Embedding.Length == 0)
                emptyEmbeddings++;
        }

        if (emptyEmbeddings > 0)
        {
            console.WriteMarkupLine(
                $"[yellow]⚠[/] {emptyEmbeddings} chunk{(emptyEmbeddings == 1 ? "" : "s")} have an empty embedding. " +
                "These rows will never be useful in a similarity query. " +
                "Consider removing them or re‑indexing with a valid embedding provider.");
        }
        else
        {
            console.WriteMarkupLine(
                $"[green]✔[/] No chunks with an empty embedding found.");
        }
    }

    private async Task<bool> CheckDatabaseConnection(string dbPath, CancellationToken ct)
    {
        if (!await db.Database.CanConnectAsync(ct))
        {
            console.WriteMarkupLine(
                "[red]✖[/] Could not connect to database at [cyan]{0}[/].",
                Markup.Escape(dbPath));
            console.WriteLine();
            return false;
        }

        return true;
    }

    private void CheckDatabasePath(string dbPath, string? configuredDbPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredDbPath) &&
            !PathsEqual(dbPath, configuredDbPath))
        {
            var effective = Path.GetFullPath(dbPath);
            var configuredResolved = Path.GetFullPath(NormalizePathWithTilde(configuredDbPath));

            console.WriteMarkupLine(
                "[yellow]⚠[/] Effective DB path ([cyan]{0}[/]) differs from configured Database:Path ([cyan]{1}[/]).",
                Markup.Escape(effective),
                Markup.Escape(configuredResolved));
        }
    }

    private bool CheckDatabaseExits(string dbPath)
    {
        if (fs.FileExists(dbPath))
        {
            console.WriteMarkupLine(
                "[green]✔[/] Database located at [cyan]{0}[/]",
                Markup.Escape(dbPath));

            return true;
        }

        console.WriteMarkupLine(
            "[yellow]⚠[/] No index database found at [yellow]{0}[/]. " +
            "Run [cyan]`minorag index`[/] to create one.",
            Markup.Escape(dbPath));
        console.WriteLine();
        return false;
    }

    private async Task RunIndexHealthChecksAsync(CancellationToken ct)
    {
        console.WriteMarkupLine("[bold]Indexed Repositories Health[/]");

        try
        {
            var summary = await indexPruner.PruneAsync(
                dryRun: true,
                pruneOrphanOwners: false,
                ct);

            if (summary.IndexEmpty)
            {
                console.WriteMarkupLine("[yellow]No repositories indexed yet. Index is empty.[/]");
                console.WriteLine();
                return;
            }

            console.WriteMarkupLine(
                "[green]✔[/] {0} repositories, {1} chunks, {2} clients, {3} projects in index",
                summary.TotalRepositories,
                summary.TotalChunks,
                summary.TotalClients,
                summary.TotalProjects);

            if (summary.OrphanChunks == 0)
            {
                console.WriteMarkupLine("[green]✔[/] No orphaned chunks");
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] There are chunks referenced to deleted repository. Run [cyan]`minorag prune`[/] to clean.",
                    summary.MissingRepositories);
            }

            if (summary.MissingRepositories == 0)
            {
                console.WriteMarkupLine("[green]✔[/] All repository directories still exist on disk");
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] {0} repositories no longer exist on disk. Run [cyan]`minorag prune`[/] to clean.",
                    summary.MissingRepositories);
            }

            if (summary.OrphanedFileRecords == 0)
            {
                console.WriteMarkupLine("[green]✔[/] All indexed files still exist on disk");
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠ {0} indexed files no longer exist on disk. Run [cyan]`minorag prune`[/] or re-index affected repos.[/]",
                    summary.OrphanedFileRecords);

                if (summary.MissingFileSamples is { Count: > 0 })
                {
                    var toShow = summary.MissingFileSamples.ToList();

                    foreach (var sample in toShow)
                    {
                        console.WriteMarkupLine(
                            "    [yellow]⚠[/] Missing file [cyan]{0}[/] (repo root [blue]{1}[/])",
                            Markup.Escape(sample.RelativePath),
                            Markup.Escape(sample.RepositoryRoot));
                    }

                    var remaining = summary.OrphanedFileRecords - toShow.Count;
                    if (remaining > 0)
                    {
                        console.WriteMarkupLine(
                            "    [grey]… and {0} more. Run [cyan]`minorag prune`[/] to refresh the index.[/]",
                            remaining);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            console.WriteMarkupLine(
                "[red]✖[/] Error while checking index health: [yellow]{0}[/]",
                Markup.Escape(ex.Message));
        }

        AnsiConsole.WriteLine();
    }

    // ------------------------------------------------------------------------
    // Ignore rules
    // ------------------------------------------------------------------------
    private async Task RunIgnoreRulesChecks(string workingDirectory, CancellationToken ct)
    {
        console.WriteMarkupLine("[bold]Ignore Rules (.minoragignore)[/]");

        try
        {
            var ignorePath = Path.Combine(workingDirectory, ".minoragignore");

            if (!fs.FileExists(ignorePath))
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] No .minoragignore found in current directory ([cyan]{0}[/]).",
                    Markup.Escape(workingDirectory));
                AnsiConsole.WriteLine();
                return;
            }

            var lines = await fs.ReadAllLinesAsync(ignorePath, ct);
            var invalid = new List<string>();

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.Contains('[') && !line.Contains(']'))
                {
                    invalid.Add(line);
                }
            }

            if (invalid.Count == 0)
            {
                console.WriteMarkupLine(
                    "[green]✔[/] .minoragignore parsed successfully ([cyan]{0}[/] rules checked)",
                    lines.Length);
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] .minoragignore contains [cyan]{0}[/] invalid rules:",
                    invalid.Count);
                foreach (var rule in invalid)
                {
                    console.WriteMarkupLine(
                        "    [yellow]⚠[/] Invalid ignore rule [red]{0}[/], ignoring this pattern.",
                        Markup.Escape(rule));
                }
            }
        }
        catch (Exception ex)
        {
            console.WriteMarkupLine(
                "[yellow]⚠[/] Failed to read .minoragignore: [yellow]{0}[/]. Continuing.",
                Markup.Escape(ex.Message));
        }

        AnsiConsole.WriteLine();
    }

    // ------------------------------------------------------------------------
    // Ollama environment
    // ------------------------------------------------------------------------

    private async Task RunOllamaChecksAsync(CancellationToken ct)
    {
        console.WriteMarkupLine("[bold]Ollama Environment[/]");

        var host = string.IsNullOrWhiteSpace(ollamaOpts.Host)
            ? "http://127.0.0.1:11434"
            : ollamaOpts.Host;

        console.WriteMarkupLine(
            "[grey]Host:[/] [cyan]{0}[/]",
            Markup.Escape(host));

        try
        {
            var http = httpClientFactory.CreateClient("minorag-doctor-ollama");
            http.BaseAddress = new Uri(host);
            http.Timeout = TimeSpan.FromSeconds(2);

            using var response = await http.GetAsync("/api/tags", ct);

            if (!response.IsSuccessStatusCode)
            {
                console.WriteMarkupLine(
                    "[red]✖[/] Ollama reachable but returned HTTP [yellow]{0}[/].",
                    (int)response.StatusCode);
                AnsiConsole.WriteLine();
                return;
            }

            console.WriteMarkupLine(
                "[green]✔[/] Ollama reachable at [cyan]{0}[/]",
                Markup.Escape(host));

            var json = await response.Content.ReadAsStringAsync(ct);
            var modelNames = ParseOllamaModelNames(json);

            CheckModelInstalled(modelNames, ollamaOpts.ChatModel, "Chat model");
            if (!string.IsNullOrWhiteSpace(ollamaOpts.AdvancedChatModel))
            {
                CheckModelInstalled(modelNames, ollamaOpts.AdvancedChatModel, "Advanced chat model");
            }
            CheckModelInstalled(modelNames, ollamaOpts.EmbeddingModel, "Embedding model");
        }
        catch (TaskCanceledException)
        {
            console.WriteMarkupLine(
                "[red]✖[/] Ollama did not respond in time. Is [cyan]`ollama serve`[/] running?");
        }
        catch (Exception)
        {
            console.WriteMarkupLine(
                "[red]✖[/] Ollama is not running or unreachable. Start it with [cyan]`ollama serve`[/].");
        }

        AnsiConsole.WriteLine();
    }

    private void RunConfigChecks(string dbPath, string? configuredDbPath)
    {
        console.WriteMarkupLine("[bold]Minorag Configuration[/]");

        if (string.IsNullOrWhiteSpace(ollamaOpts.ChatModel))
        {
            console.WriteMarkupLine(
                "[red]✖[/] Chat model is not configured. Set [cyan]Ollama:ChatModel[/] in appsettings or [cyan]MINORAG_OLLAMA__CHATMODEL[/].");
        }
        else
        {
            console.WriteMarkupLine(
                "[green]✔[/] Chat model: [cyan]{0}[/]",
                Markup.Escape(ollamaOpts.ChatModel));
        }

        if (string.IsNullOrWhiteSpace(ollamaOpts.EmbeddingModel))
        {
            console.WriteMarkupLine(
                "[red]✖[/] Embedding model is not configured. Set [cyan]Ollama:EmbeddingModel[/] in appsettings or [cyan]MINORAG_OLLAMA__EMBEDDINGMODEL[/].");
        }
        else
        {
            console.WriteMarkupLine(
                "[green]✔[/] Embedding model: [cyan]{0}[/]",
                Markup.Escape(ollamaOpts.EmbeddingModel));
        }

        if (ollamaOpts.Temperature is < 0 or > 2)
        {
            console.WriteMarkupLine(
                "[yellow]⚠[/] Temperature ([cyan]{0}[/]) looks unusual. Typical values are 0.0–1.0.",
                ollamaOpts.Temperature);
        }
        else
        {
            console.WriteMarkupLine(
                "[green]✔[/] Temperature: [cyan]{0}[/]",
                ollamaOpts.Temperature);
        }

        if (string.IsNullOrWhiteSpace(configuredDbPath))
        {
            console.WriteMarkupLine(
                "[yellow]⚠[/] Database:Path is not configured. Using default path resolved by Minorag.");
        }
        else
        {
            console.WriteMarkupLine(
                "[green]✔[/] Configured Database:Path: [cyan]{0}[/]",
                Markup.Escape(configuredDbPath));
        }

        try
        {
            var fileInfo = new FileInfo(dbPath);
            var dir = fileInfo.Directory;

            if (dir is null || !dir.Exists)
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] DB directory [cyan]{0}[/] does not exist yet. It will be created on first index run.",
                    Markup.Escape(fileInfo.DirectoryName ?? "(unknown)"));
            }
            else if (fileInfo.Exists)
            {
                using var fs = new FileStream(
                    fileInfo.FullName,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite);

                console.WriteMarkupLine(
                    "[green]✔[/] CLI appears to have read/write access to DB directory.");
            }
            else
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] DB file does not exist yet. " +
                    "Permissions will be validated when [cyan]`minorag index`[/] creates it.");
            }
        }
        catch (Exception ex)
        {
            console.WriteMarkupLine(
                "[yellow]⚠[/] Could not verify write permissions for DB directory: [yellow]{0}[/]",
                Markup.Escape(ex.Message));
        }

        var env = Environment.GetEnvironmentVariables();

        CheckEnvOverride("MINORAG_OLLAMA__HOST", ollamaOpts.Host);
        CheckEnvOverride("MINORAG_OLLAMA__CHATMODEL", ollamaOpts.ChatModel);
        CheckEnvOverride("MINORAG_OLLAMA__ADVANCEDCHATMODEL", ollamaOpts.AdvancedChatModel);
        CheckEnvOverride("MINORAG_OLLAMA__EMBEDDINGMODEL", ollamaOpts.EmbeddingModel);
        CheckEnvOverride("MINORAG_DATABASE__PATH", configuredDbPath);

        console.WriteMarkupLine("[green]✔[/] MINORAG_* environment overrides checked.");
        console.WriteLine();

        void CheckEnvOverride(string envName, string? configValue)
        {
            if (!env.Contains(envName))
                return;

            var envValue = env[envName]?.ToString() ?? string.Empty;

            if (!string.Equals(envValue, configValue, StringComparison.Ordinal))
            {
                console.WriteMarkupLine(
                    "[yellow]⚠[/] Environment override [cyan]{0}[/] = [blue]{1}[/] differs from config value ([blue]{2}[/]).",
                    envName,
                    Markup.Escape(envValue),
                    Markup.Escape(configValue ?? "(null)"));
            }
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        var na = Path.GetFullPath(NormalizePathWithTilde(a));
        var nb = Path.GetFullPath(NormalizePathWithTilde(b));

        return string.Equals(
            na,
            nb,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static HashSet<string> ParseOllamaModelNames(string json)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("models", out var modelsElem) &&
                modelsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in modelsElem.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameProp) &&
                        nameProp.ValueKind == JsonValueKind.String)
                    {
                        AddName(nameProp.GetString()!);
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in root.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameProp) &&
                        nameProp.ValueKind == JsonValueKind.String)
                    {
                        AddName(nameProp.GetString()!);
                    }
                }
            }
        }
        catch
        {
            // best-effort; treat as "no models"
        }

        return names;

        void AddName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return;

            names.Add(fullName);

            var baseName = fullName.Split(':')[0];
            names.Add(baseName);
        }
    }

    private void CheckModelInstalled(HashSet<string> models, string? model, string label)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            console.WriteMarkupLine(
                "[red]✖[/] {0} is not configured.",
                label);
            return;
        }

        var modelEscaped = Markup.Escape(model);
        if (models.Contains(model))
        {
            console.WriteMarkupLine(
                "[green]✔[/] {0} [cyan]{1}[/] installed",
                label,
                modelEscaped);
        }
        else
        {
            console.WriteMarkupLine(
                "[yellow]⚠[/] Model [cyan]{0}[/] is not installed. Run: [cyan]`ollama pull {1}`[/]",
                modelEscaped,
                modelEscaped);
        }
    }

    private static string NormalizePathWithTilde(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (path == Tilde)
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith(Tilde + Path.DirectorySeparatorChar) ||
            path.StartsWith(Tilde + Path.AltDirectorySeparatorChar))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var rest = path[2..];
            return Path.Combine(home, rest);
        }

        return path;
    }
}