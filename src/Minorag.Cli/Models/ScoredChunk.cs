using Minorag.Cli.Models.Domain;

namespace Minorag.Cli.Models;

public sealed record ScoredChunk(CodeChunk Chunk, float Score);
