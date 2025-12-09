using Minorag.Cli.Providers;

namespace Minorag.Cli.Tests.TestInfrastructure;

public class FakeEmbeddingProvider : IEmbeddingProvider
{
    public float[] EmbeddingToReturn { get; set; } = [];
    public string? LastText { get; private set; }

    public virtual Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        LastText = text;
        return Task.FromResult(EmbeddingToReturn);
    }
}