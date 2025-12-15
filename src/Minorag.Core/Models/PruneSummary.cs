namespace Minorag.Core.Models;

public sealed record PruneSummary(
    bool DatabaseMissing,
    bool IndexEmpty,
    long TotalRepositories,
    long TotalChunks,
    long TotalClients,
    long TotalProjects,
    int MissingRepositories,
    int OrphanedFileRecords,
    int OrphanChunks,
    int OrphanProjects,
    int OrphanClients,
    IReadOnlyList<MissingFileRecord> MissingFileSamples);

