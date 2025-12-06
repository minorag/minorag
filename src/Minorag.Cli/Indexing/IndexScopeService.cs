using Microsoft.EntityFrameworkCore;
using Minorag.Cli.Models.Domain;
using Minorag.Cli.Store;
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

public sealed class IndexScopeService(RagDbContext db) : IIndexScopeService
{
    public async Task<Repository> EnsureClientProjectRepoAsync(
        DirectoryInfo repoRoot,
        string? clientName,
        string? projectName,
        CancellationToken ct)
    {
        var normalizedRoot = repoRoot.FullName;

        var client = await ResolveClientAsync(clientName, ct);

        var projectResolution = await ResolveProjectAsync(projectName, client, ct);
        client ??= projectResolution.Client;
        var project = projectResolution.Project;

        var repo = await EnsureRepositoryAsync(
            normalizedRoot,
            repoRoot.Name,
            project,
            client,
            ct);

        PrintScopeSummary(repo, project, client);

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

        AnsiConsole.MarkupLine(
            $"[green]➕ Created client[/] [cyan]{Escape(client.Name)}[/] ([grey]{client.Slug}[/])");

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

        AnsiConsole.MarkupLine(
            $"[green]➕ Created project[/] [cyan]{Escape(project.Name)}[/] " +
            $"for client [blue]{Escape(owningClient.Name)}[/] ([grey]{project.Slug}[/])");

        return new ProjectResolution(project, owningClient);
    }

    private async Task<Client> EnsureDefaultClientAsync(CancellationToken ct)
    {
        const string defaultClientName = "Local";
        var defaultSlug = Slugify(defaultClientName);

        var existing = await db.Clients
            .FirstOrDefaultAsync(c => c.Slug == defaultSlug, ct);

        if (existing is not null)
            return existing;

        var client = new Client
        {
            Name = defaultClientName,
            Slug = defaultSlug
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        AnsiConsole.MarkupLine(
            $"[green]➕ Created default client[/] [cyan]{Escape(client.Name)}[/]");

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

            AnsiConsole.MarkupLine(
                $"[green]➕ Registered repository[/] [cyan]{Escape(repo.Name)}[/]");

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

        AnsiConsole.MarkupLine(
            $"[yellow]Repository[/] [cyan]{Escape(repo.Name)}[/] " +
            $"[yellow]is currently attached to[/] " +
            $"project [blue]{Escape(oldProjectName)}[/] (client [magenta]{Escape(oldClientName)}[/]).");

        AnsiConsole.Markup(
            $"Reassign to project [blue]{Escape(desiredProject.Name)}[/] " +
            $"(client [magenta]{Escape(newClientName)}[/])? " +
            "[grey][[y/N]]:[/] ");

        var answer = Console.ReadLine();
        if (!IsYes(answer))
        {
            AnsiConsole.MarkupLine("[yellow]Aborting indexing.[/] Repository mapping was not changed.");
            throw new OperationCanceledException("User declined repository reassignment.");
        }

        repo.ProjectId = desiredProject.Id;
        await db.SaveChangesAsync(ct);

        AnsiConsole.MarkupLine(
            $"[green]✔ Repository[/] [cyan]{Escape(repo.Name)}[/] " +
            $"[green]reassigned to project[/] [blue]{Escape(desiredProject.Name)}[/].");
    }

    private static void PrintScopeSummary(Repository repo, Project? project, Client? client)
    {
        var repoPart = $"[cyan]{Escape(repo.Name)}[/] (id={repo.Id})";
        var projectPart = project is not null
            ? $"project [blue]{Escape(project.Name)}[/]"
            : "project [grey](none)[/]";
        var clientPart = client is not null
            ? $"client [magenta]{Escape(client.Name)}[/]"
            : "client [grey](none)[/]";

        AnsiConsole.MarkupLine(
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

    private static string Escape(string? text)
        => text is null ? string.Empty : Markup.Escape(text);

    private sealed record ProjectResolution(Project? Project, Client? Client);
}