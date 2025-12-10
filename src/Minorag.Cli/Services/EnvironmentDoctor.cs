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
    IConfiguration configuration,
    IOptions<OllamaOptions> ollamaOptions,
    IHttpClientFactory httpClientFactory,
    IIndexPruner indexPruner) : IEnvironmentDoctor
{
    private readonly OllamaOptions ollamaOpts = ollamaOptions.Value;

    public async Task RunAsync(string dbPath, string workingDirectory, CancellationToken ct)
    {
        var configuredDbPath = configuration.GetSection("Database")["Path"];

        await RunDatabaseChecksAsync(dbPath, configuredDbPath, ct);
        await RunIndexHealthChecksAsync(ct);
        RunIgnoreRulesChecks(workingDirectory);
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
        AnsiConsole.MarkupLine("[bold]Database & Schema[/]");

        if (File.Exists(dbPath))
        {
            AnsiConsole.MarkupLine(
                "[green]✔[/] Database located at [cyan]{0}[/]",
                Markup.Escape(dbPath));
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[yellow]⚠[/] No index database found at [yellow]{0}[/]. " +
                "Run [cyan]`minorag index`[/] to create one.",
                Markup.Escape(dbPath));
            AnsiConsole.WriteLine();
            return;
        }

        if (!string.IsNullOrWhiteSpace(configuredDbPath) &&
            !PathsEqual(dbPath, configuredDbPath))
        {
            var effective = Path.GetFullPath(dbPath);
            var configuredResolved = Path.GetFullPath(NormalizePathWithTilde(configuredDbPath));

            AnsiConsole.MarkupLine(
                "[yellow]⚠[/] Effective DB path ([cyan]{0}[/]) differs from configured Database:Path ([cyan]{1}[/]).",
                Markup.Escape(effective),
                Markup.Escape(configuredResolved));
        }

        try
        {
            if (!await db.Database.CanConnectAsync(ct))
            {
                AnsiConsole.MarkupLine(
                    "[red]✖[/] Could not connect to database at [cyan]{0}[/].",
                    Markup.Escape(dbPath));
                AnsiConsole.WriteLine();
                return;
            }

            AnsiConsole.MarkupLine("[green]✔[/] SQLite connection opened via EF Core");

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
                AnsiConsole.MarkupLine(
                    "[green]✔[/] Required tables present (clients, projects, repositories, chunks)");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[red]✖[/] Database schema is missing tables: [yellow]{0}[/].",
                    string.Join(", ", missing));
                AnsiConsole.MarkupLine(
                    "    [dim]Hint: run any Minorag CLI command (e.g. [cyan]`minorag index`[/]) " +
                    "to trigger EF Core migrations.[/]");
            }

            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            if (applied.Count > 0)
            {
                AnsiConsole.MarkupLine(
                    "[green]✔[/] Schema migration history found ([cyan]{0}[/] migrations applied).",
                    applied.Count);
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[yellow]⚠[/] No EF migrations history detected. " +
                    "Schema might be outdated. Run [cyan]`minorag index`[/] once to migrate.");
            }

            if (pending.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]✔[/] Schema up-to-date (no pending migrations)");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[yellow]⚠[/] {0} pending migrations detected. Run [cyan]`minorag index`[/] to apply them.",
                    pending.Count);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                "[red]✖[/] Error while checking database: [yellow]{0}[/]",
                Markup.Escape(ex.Message));
            AnsiConsole.MarkupLine(
                "    [dim]Recovery hint: backup then remove index.db and re-run [cyan]`minorag index`[/] to rebuild.[/]");
        }

        AnsiConsole.WriteLine();
    }

    private async Task RunIndexHealthChecksAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[bold]Indexed Repositories Health[/]");

        try
        {
            var summary = await indexPruner.PruneAsync(
                dryRun: true,
                pruneOrphanOwners: false,
                ct);

            if (summary.IndexEmpty)
            {
                AnsiConsole.MarkupLine("[yellow]No repositories indexed yet. Index is empty.[/]");
                AnsiConsole.WriteLine();
                return;
            }

            AnsiConsole.MarkupLine(
                "[green]✔[/] {0} repositories, {1} chunks, {2} clients, {3} projects in index",
                summary.TotalRepositories,
                summary.TotalChunks,
                summary.TotalClients,
                summary.TotalProjects);

            if (summary.MissingRepositories == 0)
            {
                AnsiConsole.MarkupLine("[green]✔[/] All repository directories still exist on disk");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[yellow]⚠[/] {0} repositories no longer exist on disk. Run [cyan]`minorag prune`[/] to clean.",
                    summary.MissingRepositories);
            }

            if (summary.OrphanedFileRecords == 0)
            {
                AnsiConsole.MarkupLine("[green]✔[/] All indexed files still exist on disk");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[yellow]⚠ {0} indexed files no longer exist on disk. Run [cyan]`minorag prune`[/] or re-index affected repos.[/]",
                    summary.OrphanedFileRecords);

                if (summary.MissingFileSamples is { Count: > 0 })
                {
                    var toShow = summary.MissingFileSamples.ToList();

                    foreach (var sample in toShow)
                    {
                        AnsiConsole.MarkupLine(
                            "    [yellow]⚠[/] Missing file [cyan]{0}[/] (repo root [blue]{1}[/])",
                            Markup.Escape(sample.RelativePath),
                            Markup.Escape(sample.RepositoryRoot));
                    }

                    var remaining = summary.OrphanedFileRecords - toShow.Count;
                    if (remaining > 0)
                    {
                        AnsiConsole.MarkupLine(
                            "    [grey]… and {0} more. Run [cyan]`minorag prune`[/] to refresh the index.[/]",
                            remaining);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                "[red]✖[/] Error while checking index health: [yellow]{0}[/]",
                Markup.Escape(ex.Message));
        }

        AnsiConsole.WriteLine();
    }

    // ------------------------------------------------------------------------
    // Ignore rules
    // ------------------------------------------------------------------------
    private static void RunIgnoreRulesChecks(string workingDirectory)
    {
        AnsiConsole.MarkupLine("[bold]Ignore Rules (.minoragignore)[/]");

        try
        {
            var ignorePath = Path.Combine(workingDirectory, ".minoragignore");

            if (!File.Exists(ignorePath))
            {
                AnsiConsole.MarkupLine(
                    "[yellow]⚠[/] No .minoragignore found in current directory ([cyan]{0}[/]).",
                    Markup.Escape(workingDirectory));
                AnsiConsole.WriteLine();
                return;
            }

            var lines = File.ReadAllLines(ignorePath);
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
                AnsiConsole.MarkupLine(
                    "[green]✔[/] .minoragignore parsed successfully ([cyan]{0}[/] rules checked)",
                    lines.Length);
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[yellow]⚠[/] .minoragignore contains [cyan]{0}[/] invalid rules:",
                    invalid.Count);
                foreach (var rule in invalid)
                {
                    AnsiConsole.MarkupLine(
                        "    [yellow]⚠[/] Invalid ignore rule [red]{0}[/], ignoring this pattern.",
                        Markup.Escape(rule));
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
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
        AnsiConsole.MarkupLine("[bold]Ollama Environment[/]");

        var host = string.IsNullOrWhiteSpace(ollamaOpts.Host)
            ? "http://127.0.0.1:11434"
            : ollamaOpts.Host;

        AnsiConsole.MarkupLine(
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
                AnsiConsole.MarkupLine(
                    "[red]✖[/] Ollama reachable but returned HTTP [yellow]{0}[/].",
                    (int)response.StatusCode);
                AnsiConsole.WriteLine();
                return;
            }

            AnsiConsole.MarkupLine(
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
            AnsiConsole.MarkupLine(
                "[red]✖[/] Ollama did not respond in time. Is [cyan]`ollama serve`[/] running?");
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine(
                "[red]✖[/] Ollama is not running or unreachable. Start it with [cyan]`ollama serve`[/].");
        }

        AnsiConsole.WriteLine();
    }

    private void RunConfigChecks(string dbPath, string? configuredDbPath)
    {
        AnsiConsole.MarkupLine("[bold]Minorag Configuration[/]");

        if (string.IsNullOrWhiteSpace(ollamaOpts.ChatModel))
        {
            AnsiConsole.MarkupLine(
                "[red]✖[/] Chat model is not configured. Set [cyan]Ollama:ChatModel[/] in appsettings or [cyan]MINORAG_OLLAMA__CHATMODEL[/].");
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[green]✔[/] Chat model: [cyan]{0}[/]",
                Markup.Escape(ollamaOpts.ChatModel));
        }

        if (string.IsNullOrWhiteSpace(ollamaOpts.EmbeddingModel))
        {
            AnsiConsole.MarkupLine(
                "[red]✖[/] Embedding model is not configured. Set [cyan]Ollama:EmbeddingModel[/] in appsettings or [cyan]MINORAG_OLLAMA__EMBEDDINGMODEL[/].");
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[green]✔[/] Embedding model: [cyan]{0}[/]",
                Markup.Escape(ollamaOpts.EmbeddingModel));
        }

        if (ollamaOpts.Temperature is < 0 or > 2)
        {
            AnsiConsole.MarkupLine(
                "[yellow]⚠[/] Temperature ([cyan]{0}[/]) looks unusual. Typical values are 0.0–1.0.",
                ollamaOpts.Temperature);
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[green]✔[/] Temperature: [cyan]{0}[/]",
                ollamaOpts.Temperature);
        }

        if (string.IsNullOrWhiteSpace(configuredDbPath))
        {
            AnsiConsole.MarkupLine(
                "[yellow]⚠[/] Database:Path is not configured. Using default path resolved by Minorag.");
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[green]✔[/] Configured Database:Path: [cyan]{0}[/]",
                Markup.Escape(configuredDbPath));
        }

        try
        {
            var fileInfo = new FileInfo(dbPath);
            var dir = fileInfo.Directory;

            if (dir is null || !dir.Exists)
            {
                AnsiConsole.MarkupLine(
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

                AnsiConsole.MarkupLine(
                    "[green]✔[/] CLI appears to have read/write access to DB directory.");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[yellow]⚠[/] DB file does not exist yet. " +
                    "Permissions will be validated when [cyan]`minorag index`[/] creates it.");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                "[yellow]⚠[/] Could not verify write permissions for DB directory: [yellow]{0}[/]",
                Markup.Escape(ex.Message));
        }

        var env = Environment.GetEnvironmentVariables();

        CheckEnvOverride("MINORAG_OLLAMA__HOST", ollamaOpts.Host);
        CheckEnvOverride("MINORAG_OLLAMA__CHATMODEL", ollamaOpts.ChatModel);
        CheckEnvOverride("MINORAG_OLLAMA__ADVANCEDCHATMODEL", ollamaOpts.AdvancedChatModel);
        CheckEnvOverride("MINORAG_OLLAMA__EMBEDDINGMODEL", ollamaOpts.EmbeddingModel);
        CheckEnvOverride("MINORAG_DATABASE__PATH", configuredDbPath);

        AnsiConsole.MarkupLine("[green]✔[/] MINORAG_* environment overrides checked.");
        AnsiConsole.WriteLine();

        void CheckEnvOverride(string envName, string? configValue)
        {
            if (!env.Contains(envName))
                return;

            var envValue = env[envName]?.ToString() ?? string.Empty;

            if (!string.Equals(envValue, configValue, StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine(
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

    private static void CheckModelInstalled(HashSet<string> models, string? model, string label)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            AnsiConsole.MarkupLine(
                "[red]✖[/] {0} is not configured.",
                label);
            return;
        }

        if (models.Contains(model))
        {
            AnsiConsole.MarkupLine(
                "[green]✔[/] {0} [cyan]{1}[/] installed",
                label,
                Markup.Escape(model));
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[yellow]⚠[/] Model [cyan]{0}[/] is not installed. Run: [cyan]`ollama pull {1}`[/]",
                Markup.Escape(model),
                Markup.Escape(model));
        }
    }

    private static string NormalizePathWithTilde(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~" + Path.DirectorySeparatorChar) ||
            path.StartsWith("~" + Path.AltDirectorySeparatorChar))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var rest = path[2..];
            return Path.Combine(home, rest);
        }

        return path;
    }
}