using Microsoft.EntityFrameworkCore;
using Minorag.Core.Models.Domain;
using Minorag.Core.Services;
using Minorag.Core.Store;
using Spectre.Console;

namespace Minorag.Cli.Indexing;

public interface IIndexScopeService
{
    Task<Repository> EnsureClientProjectRepoAsync(
        DirectoryInfo repoRoot,
        string? clientName,
        string? projectName,
        CancellationToken ct);
}

public sealed class IndexScopeService(RagDbContext db, IMinoragConsole console) : IIndexScopeService
{
    public async Task<Repository> EnsureClientProjectRepoAsync(
         DirectoryInfo repoRoot,
         string? clientName,
         string? projectName,
         CancellationToken ct)
    {
        var normalizedRoot = repoRoot.FullName;

        // 1. Resolve client (from CLI flag, if provided)
        var client = await ResolveClientAsync(clientName, ct);

        // 2. Resolve project (may create default "Local" client)
        var projectResolution = await ResolveProjectAsync(projectName, client, ct);
        client ??= projectResolution.Client;
        var project = projectResolution.Project;

        // 3. Resolve / create repo and handle reassignment
        var repo = await EnsureRepositoryAsync(
            normalizedRoot,
            repoRoot.Name,
            project,
            client,
            ct);

        // 4. Use the *actual* attached project/client for the summary
        var actualProject = repo.Project ?? project;
        var actualClient = repo.Project?.Client ?? client;

        PrintScopeSummary(repo, actualProject, actualClient);

        return repo;
    }

    private async Task<Client?> ResolveClientAsync(string? clientName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            return null;

        clientName = clientName.Trim();
        var slug = Slugify(clientName);

        var existing = await db.Clients
            .FirstOrDefaultAsync(c => c.Slug == slug, ct);

        if (existing is not null)
            return existing;

        var client = new Client
        {
            Name = clientName,
            Slug = slug
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        console.WriteMarkupLine(
            $"[green]➕ Created client[/] [cyan]{client.Name}[/] ([grey]{client.Slug}[/])");

        return client;
    }


    private async Task<ProjectResolution> ResolveProjectAsync(
        string? projectName,
        Client? client,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return new ProjectResolution(null, client);

        projectName = projectName.Trim();
        var slug = Slugify(projectName);

        Project? project;
        Client? owningClient = client;

        if (owningClient is not null)
        {
            project = await db.Projects
                .FirstOrDefaultAsync(
                    p => p.Slug == slug && p.ClientId == owningClient.Id,
                    ct);
        }
        else
        {
            project = await db.Projects
                .Include(p => p.Client)
                .FirstOrDefaultAsync(p => p.Slug == slug, ct);

            owningClient ??= project?.Client;
        }

        if (project is not null)
            return new ProjectResolution(project, owningClient);

        owningClient ??= await EnsureDefaultClientAsync(ct);

        project = new Project
        {
            Name = projectName,
            Slug = slug,
            ClientId = owningClient.Id
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        console.WriteMarkupLine(
            $"[green]➕ Created project[/] [cyan]{project.Name}[/] " +
            $"for client [blue]{owningClient.Name}[/] ([grey]{project.Slug}[/])");

        return new ProjectResolution(project, owningClient);
    }

    private async Task<Client> EnsureDefaultClientAsync(CancellationToken ct)
    {
        const string defaultClientName = "Local";
        var defaultSlug = Slugify(defaultClientName);

        var existing = await db.Clients
            .FirstOrDefaultAsync(c => c.Slug == defaultSlug, ct);

        if (existing is not null)
        {
            return existing;
        }

        var client = new Client
        {
            Name = defaultClientName,
            Slug = defaultSlug
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        console.WriteMarkupLine($"[green]➕ Created default client[/] [cyan]{client.Name}[/]");

        return client;
    }

    private async Task<Repository> EnsureRepositoryAsync(
        string rootPath,
        string repoName,
        Project? desiredProject,
        Client? desiredClient,
        CancellationToken ct)
    {
        rootPath = Path.GetFullPath(rootPath);

        var repo = await db.Repositories
            .Include(r => r.Project!)
            .ThenInclude(p => p.Client)
            .FirstOrDefaultAsync(r => r.RootPath == rootPath, ct);

        if (repo is null)
        {
            repo = new Repository
            {
                RootPath = rootPath,
                Name = repoName,
                ProjectId = desiredProject?.Id
            };

            db.Repositories.Add(repo);
            await db.SaveChangesAsync(ct);

            console.WriteMarkupLine(
                $"[green]➕ Registered repository[/] [cyan]{repo.Name}[/]");

            return repo;
        }

        if (desiredProject is null || repo.ProjectId == desiredProject.Id)
            return repo;

        await PromptAndReassignRepoAsync(repo, desiredProject, desiredClient, ct);

        return repo;
    }

    private async Task PromptAndReassignRepoAsync(
        Repository repo,
        Project desiredProject,
        Client? desiredClient,
        CancellationToken ct)
    {
        var oldProjectName = repo.Project?.Name ?? "(none)";
        var oldClientName = repo.Project?.Client?.Name ?? "(none)";
        var newClientName = desiredClient?.Name ?? oldClientName;

        console.WriteMarkupLine(
            $"[yellow]Repository[/] [cyan]{repo.Name}[/] " +
            $"[yellow]is currently attached to[/] " +
            $"project [blue]{oldProjectName}[/] (client [magenta]{oldClientName}[/]).");

        AnsiConsole.Markup(
            $"Reassign to project [blue]{desiredProject.Name}[/] " +
            $"(client [magenta]{newClientName}[/])? " +
            "[grey][[y/N]]:[/] ");

        var answer = Console.ReadLine();
        if (!IsYes(answer))
        {
            console.WriteMarkupLine("[yellow]Aborting indexing.[/] Repository mapping was not changed.");
            throw new OperationCanceledException("User declined repository reassignment.");
        }

        repo.ProjectId = desiredProject.Id;
        await db.SaveChangesAsync(ct);

        console.WriteMarkupLine(
            $"[green]✔ Repository[/] [cyan]{repo.Name}[/] " +
            $"[green]reassigned to project[/] [blue]{desiredProject.Name}[/].");
    }

    private void PrintScopeSummary(Repository repo, Project? project, Client? client)
    {
        var repoPart = $"[cyan]{repo.Name}[/] (id={repo.Id})";
        var projectPart = project is not null
            ? $"project [blue]{project.Name}[/]"
            : "project [grey](none)[/]";
        var clientPart = client is not null
            ? $"client [magenta]{client.Name}[/]"
            : "client [grey](none)[/]";

        console.WriteMarkupLine(
            $"[grey]Scope:[/] repo {repoPart}, {projectPart}, {clientPart}");
    }

    private static bool IsYes(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();
        return string.Equals(input, "y", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed";

        name = name.Trim().ToLowerInvariant();

        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_')
            {
                if (sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
            }
        }

        while (sb.Length > 0 && sb[^1] == '-')
            sb.Length--;

        return sb.Length == 0 ? "unnamed" : sb.ToString();
    }

    private sealed record ProjectResolution(Project? Project, Client? Client);
}