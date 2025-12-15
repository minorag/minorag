namespace Minorag.Api.Models;

public record AskRequest(
    string? CurrentDirectory,
    string? Question,
    int? TopK,
    string[]? ExplicitRepoNames,
    string? ReposCsv,
    string? ProjectName,
    string? ClientName,
    bool NoLlm,
    bool Verbose,
    bool AllRepos,
    bool UseAdvancedModel);
