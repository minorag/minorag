using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Options;

namespace Minorag.Cli.Providers;

public class OllamaEmbeddingProvider(IOllamaClient ollama, IOptions<OllamaOptions> options) : IEmbeddingProvider
{
    private readonly string _model = options.Value.EmbeddingModel;

    public async Task<float[]> EmbedAsync(string? text, CancellationToken ct)
    {
        var embedding = await ollama.GetEmbeddingAsync(
            _model,
            text ?? string.Empty,
            ct);

        var floats = new float[embedding.Count];
        for (var i = 0; i < floats.Length; i++)
        {
            floats[i] = (float)embedding[i];
        }

        return floats;
    }
}