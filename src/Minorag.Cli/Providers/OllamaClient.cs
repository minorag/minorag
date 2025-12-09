using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Minorag.Cli.Providers;

public interface IOllamaClient
{
    Task<IReadOnlyList<double>> GetEmbeddingAsync(
        string model,
        string prompt,
        CancellationToken ct);

    Task<string> ChatAsync(
        string model,
        double temperature,
        string prompt,
        CancellationToken ct);

    IAsyncEnumerable<string> ChatStreamAsync(
        string model,
        double temperature,
        string prompt,
        CancellationToken ct = default);
}

public class OllamaClient(HttpClient httpClient) : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<IReadOnlyList<double>> GetEmbeddingAsync(
        string model,
        string prompt,
        CancellationToken ct)
    {
        var payload = new
        {
            model,
            prompt
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.PostAsync("/api/embeddings", requestContent, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Ollama embeddings failed ({(int)response.StatusCode} {response.StatusCode}): {body}");
        }

        var embeddingResponse = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(
            body,
            JsonOptions
        ) ?? throw new InvalidOperationException("Failed to deserialize Ollama embeddings response.");

        if (embeddingResponse.Embedding is null || embeddingResponse.Embedding.Count == 0)
        {
            throw new InvalidOperationException($"Ollama returned an empty embedding. Raw body: {body}");
        }

        return embeddingResponse.Embedding;
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
            string model,
            double temperature,
            string prompt,
            [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = new
        {
            model,
            temperature,
            stream = true,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChunk>(line, JsonOptions);
            }
            catch
            {
                continue;
            }

            if (chunk is null)
                continue;

            if (!string.IsNullOrEmpty(chunk.Message?.Content))
            {
                // Yield just the content piece
                yield return chunk.Message.Content;
            }

            if (chunk.Done)
                break;
        }
    }

    public async Task<string> ChatAsync(
        string model,
        double temperature,
        string prompt,
        CancellationToken ct)
    {
        var payload = new
        {
            model,
            temperature,
            stream = true,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var fullAnswer = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            OllamaChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChunk>(line, JsonOptions);
            }
            catch
            {
                continue;
            }

            if (chunk is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(chunk.Message?.Content))
            {
                fullAnswer.Append(chunk.Message.Content);
            }

            if (chunk.Done)
            {
                break;
            }
        }

        return fullAnswer.ToString();
    }

    // local DTOs
    private sealed record OllamaChunk
    {
        public bool Done { get; init; }
        public ChatMessage? Message { get; init; }
    }

    private sealed record ChatMessage
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
    }

    // local DTO
    sealed class OllamaEmbeddingResponse
    {
        public List<double> Embedding { get; init; } = [];
    }
}