namespace Minorag.Core.Models.Indexing;

public readonly record struct ChunkSpec(
       int MaxTokens,
       int OverlapTokens,
       int HardMaxChars,
       SplitMode Mode);
