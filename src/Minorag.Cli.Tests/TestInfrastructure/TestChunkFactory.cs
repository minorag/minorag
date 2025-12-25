using Minorag.Core.Models.Domain;

namespace Minorag.Cli.Tests.TestInfrastructure;

public static class TestChunkFactory
{
    public static CodeChunk CreateChunk(
        long id,
        float[] embedding,
        int repositoryId = 1,
        string? path = null,
        string? fileHash = null,
        string language = "csharp",
        string extension = "cs",
        string kind = "file",
        string? symbolName = null,
        int chunkIndex = 0)
    {
        path ??= $"/fake/path/{id}.{extension}";
        fileHash ??= $"hash-{id}";
        symbolName ??= $"Symbol{id}";

        var file = new RepositoryFile
        {
            RepositoryId = repositoryId,
            Path = path,
            Extension = extension,
            Language = language,
            Kind = kind,
            SymbolName = symbolName,
            Content = $"// file content for {path}",
            FileHash = fileHash
        };

        return new CodeChunk
        {
            Id = id,
            File = file,
            FileId = 0, // in-memory tests only
            Content = $"// chunk {chunkIndex} for {path}",
            Embedding = embedding,
            ChunkIndex = chunkIndex
        };
    }
}