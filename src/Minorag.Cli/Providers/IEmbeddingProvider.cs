namespace Minorag.Cli.Providers;

public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}
