using Minorag.Core.Models.Domain;

namespace Minorag.Core.Models;

public sealed record ScoredChunk(CodeChunk Chunk, float Score);
