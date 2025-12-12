using Microsoft.Extensions.Options;
using Minorag.Cli.Models.Options;

namespace Minorag.Cli.Services.Environments;

public class ConfigValidatorFactory(
    IEnvironmentHelper environment,
    IOptions<OllamaOptions> ollamaOptions)
{
    public IValidator Create(string dbPath, string? configuredDbPath)
    {
        return new ConfigValidator(environment, ollamaOptions, dbPath, configuredDbPath);
    }
}
