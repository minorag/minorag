using Minorag.Cli.Models;

namespace Minorag.Cli.Services.Environments;

public interface IValidator
{
    IAsyncEnumerable<EnvironmentCheckResult> ValidateAsync(CancellationToken ct);
}