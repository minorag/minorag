using Minorag.Core.Models.ViewModels;

namespace Minorag.Core.Models;

public sealed record ScoredChunk(CodeChunkVm Chunk, float Score);
