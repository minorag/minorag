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
        var allRepos = await LoadRepositoriesAsync(ct);

        EnsureAnyRepositories(allRepos);

        if (allReposFlag)
        {
            PrintAllReposScope();
            return allRepos;
        }

        var explicitRepoNames = BuildExplicitRepoNameSet(repoNames, reposCsv);
        var explicitScope = TryResolveExplicitRepos(explicitRepoNames, allRepos);
        if (explicitScope is not null)
        {
            return explicitScope;
        }

        var projectScope = TryResolveProjectScope(projectName, allRepos);
        if (projectScope is not null)
        {
            return projectScope;
        }

        var clientScope = TryResolveClientScope(clientName, allRepos);
        if (clientScope is not null)
        {
            return clientScope;
        }

        return ResolveFromCurrentDirectory(currentDirectory, allRepos);
    }

    private static void EnsureAnyRepositories(List<Repository> allRepos)
    {
        if (allRepos.Count == 0)
        {
            throw new InvalidOperationException(
                "No repositories found in the index. Run `minorag index` inside a project folder first.");
        }
    }

    private static void PrintAllReposScope()
    {
        AnsiConsole.MarkupLine("[cyan]Using scope:[/] [green]all repositories[/].");
    }

    private static HashSet<string> BuildExplicitRepoNameSet(
        IReadOnlyList<string> repoNames,
        string? reposCsv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in repoNames.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            set.Add(name.Trim());
        }

        if (!string.IsNullOrWhiteSpace(reposCsv))
        {
            var parts = reposCsv.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var p in parts)
            {
                set.Add(p);
            }
        }

        return set;
    }

    private async Task<List<Repository>> LoadRepositoriesAsync(CancellationToken ct)
    {
        return await db.Repositories
            .Include(r => r.Project)
            .ThenInclude(p => p.Client)
            .ToListAsync(ct);
    }

    // ------------------------------------------------------------
    // Resolution strategies (repo / project / client / cwd)
    // ------------------------------------------------------------
    private static List<Repository>? TryResolveExplicitRepos(
        HashSet<string> explicitRepoNames,
        List<Repository> allRepos)
    {
        if (explicitRepoNames.Count == 0)
        {
            return null;
        }

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

    private static IReadOnlyList<Repository>? TryResolveProjectScope(
        string? projectName,
        List<Repository> allRepos)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return null;

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

    private static IReadOnlyList<Repository>? TryResolveClientScope(
        string? clientName,
        List<Repository> allRepos)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            return null;

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

    private static IReadOnlyList<Repository> ResolveFromCurrentDirectory(
        string currentDirectory,
        List<Repository> allRepos)
    {
        var activeRepo = FindActiveRepository(currentDirectory, allRepos);

        if (activeRepo is not null)
        {
            // Repo is attached to a project → use project scope
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

            // Repo has no project → just this one
            AnsiConsole.MarkupLine(
                "[cyan]Using repository scope:[/] Repo=\"{0}\" at [grey]{1}[/]",
                activeRepo.Name,
                activeRepo.RootPath);

            return new[] { activeRepo };
        }

        // No flags + no active repo → force explicit scope with guidance
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

    // ------------------------------------------------------------
    // Path helpers
    // ------------------------------------------------------------

    private static Repository? FindActiveRepository(
        string currentDirectory,
        List<Repository> allRepos)
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

        if (fullPath.Length == fullRoot.Length)
            return true;

        var nextChar = fullPath[fullRoot.Length];
        return nextChar is '/' or '\\';
    }
}