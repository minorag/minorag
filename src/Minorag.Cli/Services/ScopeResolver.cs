using Microsoft.EntityFrameworkCore;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Store;
using Spectre.Console;

namespace Minorag.Cli.Services;

public sealed class ScopeResolver(RagDbContext db)
{
    public async Task<IReadOnlyList<Repository>> ResolveScopeAsync(
        string currentDirectory,
        IReadOnlyList<string> repoNames, // from repeated --repo
        string? reposCsv,                // from --repos
        string? projectName,             // from --project
        string? clientName,              // from --client
        bool allReposFlag,               // from --all-repos
        CancellationToken ct)
    {
        // Load all repos with project + client for in-memory filtering
        var allRepos = await db.Repositories
            .Include(r => r.Project)
            .ThenInclude(p => p.Client)
            .ToListAsync(ct);

        // No repos at all → special error
        if (allRepos.Count == 0)
        {
            throw new InvalidOperationException(
                "No repositories found in the index. Run `minorag index` inside a project folder first.");
        }

        if (allReposFlag)
        {
            AnsiConsole.MarkupLine("[cyan]Using scope:[/] [green]all repositories[/].");
            return allRepos;
        }

        var explicitRepoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in repoNames.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            explicitRepoNames.Add(name.Trim());
        }

        if (!string.IsNullOrWhiteSpace(reposCsv))
        {
            var parts = reposCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                explicitRepoNames.Add(p);
            }
        }

        if (explicitRepoNames.Count > 0)
        {
            var scoped = allRepos
                .Where(r => explicitRepoNames.Contains(r.Name))
                .ToList();

            var missing = explicitRepoNames
                .Except(scoped.Select(r => r.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count > 0)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]Warning:[/] some repositories were not found in the index: [red]{0}[/]",
                    string.Join(", ", missing));
            }

            if (scoped.Count == 0)
            {
                throw new InvalidOperationException(
                    "No matching repositories found for the provided --repo / --repos values.");
            }

            AnsiConsole.MarkupLine(
                "[cyan]Using explicit repository scope:[/] {0}",
                string.Join(", ", scoped.Select(r => r.Name)));

            return scoped;
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var name = projectName.Trim();

            var scoped = allRepos
                .Where(r => r.Project != null &&
                            string.Equals(r.Project.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (scoped.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No repositories found for project \"{name}\".");
            }

            var client = scoped.First().Project!.Client?.Name ?? "-";
            AnsiConsole.MarkupLine(
                "[cyan]Using project scope:[/] Client=\"{0}\", Project=\"{1}\" (repos: {2})",
                client,
                name,
                string.Join(", ", scoped.Select(r => r.Name)));

            return scoped;
        }

        if (!string.IsNullOrWhiteSpace(clientName))
        {
            var name = clientName.Trim();
            var scoped = allRepos
                .Where(r => r.Project?.Client != null &&
                            string.Equals(r.Project.Client.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (scoped.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No repositories found for client \"{name}\".");
            }

            AnsiConsole.MarkupLine(
                "[cyan]Using client scope:[/] Client=\"{0}\" (repos: {1})",
                name,
                string.Join(", ", scoped.Select(r => r.Name)));

            return scoped;
        }

        // 5. No explicit flags → infer from current working directory
        var activeRepo = FindActiveRepository(currentDirectory, allRepos);

        if (activeRepo is not null)
        {
            if (activeRepo.ProjectId is not null)
            {
                var sameProject = allRepos
                    .Where(r => r.ProjectId == activeRepo.ProjectId)
                    .ToList();

                var projectNameResolved = activeRepo.Project!.Name;
                var clientNameResolved = activeRepo.Project.Client?.Name ?? "-";

                AnsiConsole.MarkupLine(
                    "[cyan]Using project scope:[/] Client=\"{0}\", Project=\"{1}\" (repos: {2})",
                    clientNameResolved,
                    projectNameResolved,
                    string.Join(", ", sameProject.Select(r => r.Name)));

                return sameProject;
            }

            AnsiConsole.MarkupLine(
                "[cyan]Using repository scope:[/] Repo=\"{0}\" at [grey]{1}[/]",
                activeRepo.Name,
                activeRepo.RootPath);

            return [activeRepo];
        }

        var cwd = Path.GetFullPath(currentDirectory);

        var message = $"""
                       No active repository detected for current directory: {cwd}

                       Please specify scope explicitly, for example:
                         minorag ask "..." --repo project-api
                         minorag ask "..." --project "Project"
                         minorag ask "..." --client "Acme Corp"
                         minorag ask "..." --repos repo1,repo2
                         minorag ask "..." --all-repos
                       """;

        throw new InvalidOperationException(message);
    }

    private static Repository? FindActiveRepository(string currentDirectory, List<Repository> allRepos)
    {
        var cwd = Path.GetFullPath(currentDirectory);

        return allRepos
            .Where(r => IsSubPath(cwd, r.RootPath))
            .OrderByDescending(r => r.RootPath.Length)
            .FirstOrDefault();
    }

    private static bool IsSubPath(string path, string potentialRoot)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var fullRoot = Path.GetFullPath(potentialRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        // Either exact match or next char is a directory separator
        if (fullPath.Length == fullRoot.Length)
            return true;

        var nextChar = fullPath[fullRoot.Length];
        return nextChar is '/' or '\\';
    }
}