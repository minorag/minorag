namespace Minorag.Core.Models;

public record DatabaseStatus(
    int TotalClients,
    int TotalProjects,
    int TotalRepos,
    int TotalFiles,
    int TotalChunks,
    int OrphanedChunks);
