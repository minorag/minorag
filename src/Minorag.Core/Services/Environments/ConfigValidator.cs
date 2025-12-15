using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Minorag.Core.Models;
using Minorag.Core.Models.Options;

namespace Minorag.Core.Services.Environments;

public class ConfigValidator(
    IEnvironmentHelper environment,
    IOptions<OllamaOptions> ollamaOptions,
    string dbPath,
    string? configuredDbPath) : IValidator
{
    private readonly OllamaOptions _options = ollamaOptions.Value;

    public async IAsyncEnumerable<EnvironmentCheckResult> ValidateAsync([EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configuredDbPath))
        {
            yield return new("Database", "Path is not configured. Using default path resolved by Minorag.", EnvironmentIssueSeverity.Warning);
        }
        else
        {
            yield return new("Configured Database", $"Path: [cyan]{configuredDbPath}[/]", EnvironmentIssueSeverity.Success);
        }

        var fileInfo = new FileInfo(dbPath);
        var dir = fileInfo.Directory;

        if (dir is null || !dir.Exists)
        {
            var directory = fileInfo.DirectoryName ?? "(unknown)";
            yield return new("DB directory", $"[cyan]{directory}[/] does not exist yet. It will be created on first index run.", EnvironmentIssueSeverity.Warning);
        }
        else if (fileInfo.Exists)
        {
            bool verifyPermissions;
            string? message = null;
            try
            {
                using var fs = new FileStream(
                    fileInfo.FullName,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite);

                verifyPermissions = true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                verifyPermissions = false;
            }

            if (verifyPermissions)
            {
                yield return new("DB directory", "CLI appears to have read/write access to DB directory.", EnvironmentIssueSeverity.Success);
            }
            else
            {
                yield return new("DB directory", $"Could not verify write permissions for DB directory: [yellow]{message ?? ""}[/]", EnvironmentIssueSeverity.Error);
            }
        }
        else
        {
            yield return new("DB file does not exist yet.", "Permissions will be validated when [cyan]`minorag index`[/] creates it.", EnvironmentIssueSeverity.Warning);
        }

        var results = new EnvironmentCheckResult?[]
        {
            CheckEnvOverride("MINORAG_OLLAMA__HOST", _options.Host),
            CheckEnvOverride("MINORAG_OLLAMA__CHATMODEL", _options.ChatModel),
            CheckEnvOverride("MINORAG_OLLAMA__ADVANCEDCHATMODEL", _options.AdvancedChatModel),
            CheckEnvOverride("MINORAG_OLLAMA__EMBEDDINGMODEL", _options.EmbeddingModel),
            CheckEnvOverride("MINORAG_DATABASE__PATH", configuredDbPath)
        };

        foreach (var result in results)
        {
            if (result is not null)
            {
                yield return result;
            }
        }

        yield return new EnvironmentCheckResult("MINORAG_*", "environment overrides checked.", EnvironmentIssueSeverity.Success);

        EnvironmentCheckResult? CheckEnvOverride(string envName, string? configValue)
        {
            var (hasOverwrite, envValue) = environment.IsOverwritten(envName, configValue);
            if (hasOverwrite)
            {
                return new EnvironmentCheckResult("Environment override", $"[cyan]{envName}[/] = [blue]{envValue ?? "(null)"}[/] differs from config value ([blue]{configValue ?? "(null)"}[/]).", EnvironmentIssueSeverity.Warning);
            }

            return null;
        }
    }
}
