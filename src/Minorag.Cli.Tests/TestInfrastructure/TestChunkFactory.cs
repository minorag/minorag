using Minorag.Core.Models.Domain;

namespace Minorag.Cli.Tests;

public static class TestChunkFactory
{
    public static CodeChunk CreateChunk(
        long id,
        float[] embedding,
        int repositoryId = 1,
        string? path = null,
        string? fileHash = null,
        string? language = "csharp",
        string extension = ".cs",
        string kind = "file",
        string? symbolName = null,
        int chunkIndex = 0)
    {
        path ??= $"/fake/path/{id}{extension}";
        fileHash ??= $"hash-{id}";
        symbolName ??= $"Symbol{id}";

        return new CodeChunk
        {
            Id = id,
            Path = path,
            Extension = extension,
            Language = language!,
            Kind = kind,
            SymbolName = symbolName,
            Content = $"// content for chunk {id}",
            Embedding = embedding,
            FileHash = fileHash,
            ChunkIndex = chunkIndex,
            RepositoryId = repositoryId,
        };
    }
}