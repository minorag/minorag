using Microsoft.Extensions.Options;
using Minorag.Core.Models.Options;

namespace Minorag.Core.Services.Environments;

public class ConfigValidatorFactory(
    IEnvironmentHelper environment,
    IOptions<OllamaOptions> ollamaOptions)
{
    public IValidator Create(string dbPath, string? configuredDbPath)
    {
        return new ConfigValidator(environment, ollamaOptions, dbPath, configuredDbPath);
    }
}
