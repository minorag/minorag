using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Minorag.Core.Models;
using Minorag.Core.Models.Options;
using Minorag.Core.Providers;

namespace Minorag.Core.Services.Environments;

public class OllamaValidator(IOllamaClient client, IOptions<OllamaOptions> ollamaOptions) : IValidator
{
    private const string Label = "Ollama Check";
    private readonly OllamaOptions ollamaOpts = ollamaOptions.Value;

    public async IAsyncEnumerable<EnvironmentCheckResult> ValidateAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var (modelNames, result) = await GetTags(ct);

        yield return result;

        yield return CheckModelInstalled(modelNames, ollamaOpts.ChatModel, "Chat model");
        if (!string.IsNullOrWhiteSpace(ollamaOpts.AdvancedChatModel))
        {
            yield return CheckModelInstalled(modelNames, ollamaOpts.AdvancedChatModel, "Advanced chat model");
        }

        yield return CheckModelInstalled(modelNames, ollamaOpts.EmbeddingModel, "Embedding model");

        foreach (var optsCheck in CheckConfig())
        {
            yield return optsCheck;
        }
    }

    private IEnumerable<EnvironmentCheckResult> CheckConfig()
    {
        if (string.IsNullOrWhiteSpace(ollamaOpts.ChatModel))
        {
            yield return new(Label, "Chat model is not configured. Set [cyan]Ollama:ChatModel[/] in appsettings or [cyan]MINORAG_OLLAMA__CHATMODEL[/].", EnvironmentIssueSeverity.Error);
        }
        else
        {
            yield return new(Label, $"Chat model: [cyan]{ollamaOpts.ChatModel}[/]", EnvironmentIssueSeverity.Success);
        }

        if (string.IsNullOrWhiteSpace(ollamaOpts.EmbeddingModel))
        {
            yield return new(Label, $"Embedding model is not configured. Set [cyan]Ollama:EmbeddingModel[/] in appsettings or [cyan]MINORAG_OLLAMA__EMBEDDINGMODEL[/].", EnvironmentIssueSeverity.Error);
        }
        else
        {
            yield return new(Label, $"Embedding model: [cyan]{ollamaOpts.EmbeddingModel}[/].", EnvironmentIssueSeverity.Success);
        }

        if (ollamaOpts.Temperature is < 0 or > 2)
        {
            yield return new(Label, $"Temperature [cyan]{ollamaOpts.Temperature}[/] looks unusual. Typical values are 0.0–1.0.", EnvironmentIssueSeverity.Warning);
        }
        else
        {
            yield return new(Label, $"Temperature: [cyan]{ollamaOpts.Temperature}[/]", EnvironmentIssueSeverity.Success);
        }
    }

    private static HashSet<string> ParseOllamaModelNames(string json)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("models", out var modelsElem) &&
                modelsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in modelsElem.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameProp) &&
                        nameProp.ValueKind == JsonValueKind.String)
                    {
                        AddName(nameProp.GetString()!);
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in root.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameProp) &&
                        nameProp.ValueKind == JsonValueKind.String)
                    {
                        AddName(nameProp.GetString()!);
                    }
                }
            }
        }
        catch
        {
            // best-effort; treat as "no models"
        }

        return names;

        void AddName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return;

            names.Add(fullName);

            var baseName = fullName.Split(':')[0];
            names.Add(baseName);
        }
    }

    private static EnvironmentCheckResult CheckModelInstalled(HashSet<string> models, string? model, string label)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return new EnvironmentCheckResult(Label, $"{label} is not configured.", EnvironmentIssueSeverity.Warning);
        }

        if (models.Contains(model))
        {
            return new EnvironmentCheckResult(Label, $"{label} [cyan]{model} installed [/]", EnvironmentIssueSeverity.Success);
        }

        return new EnvironmentCheckResult(Label, $"Model [cyan]{model}[/] is not installed. Run: [cyan]`ollama pull {model}`[/]", EnvironmentIssueSeverity.Warning);
    }

    private async Task<(HashSet<string>, EnvironmentCheckResult)> GetTags(CancellationToken ct)
    {
        using var response = await client.GetTags(ct);

        if (!response.IsSuccessStatusCode)
        {
            return ([], new EnvironmentCheckResult(Label, $"Ollama reachable but returned HTTP [yellow]{(int)response.StatusCode}[/].", EnvironmentIssueSeverity.Error));
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var modelNames = ParseOllamaModelNames(json);

        return (modelNames, new EnvironmentCheckResult(Label, $" Ollama reachable at [cyan]{client.Host}[/].", EnvironmentIssueSeverity.Success));
    }
}
