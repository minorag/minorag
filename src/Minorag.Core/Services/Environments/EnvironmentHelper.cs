using System.Collections;

namespace Minorag.Core.Services.Environments;

public interface IEnvironmentHelper
{
    (bool, string?) IsOverwritten(string envName, string? configValue);
}

public class EnvironmentHelper : IEnvironmentHelper
{
    private readonly IDictionary env = Environment.GetEnvironmentVariables();
    public (bool, string?) IsOverwritten(string envName, string? configValue)
    {
        if (!env.Contains(envName))
            return (false, null);

        var envValue = env[envName]?.ToString() ?? string.Empty;
        var isOverwritten = string.Equals(envValue, configValue, StringComparison.Ordinal); ;
        return (isOverwritten, envName);
    }
}
