using Minorag.Core.Models;

namespace Minorag.Core.Services.Environments;

public interface IValidator
{
    IAsyncEnumerable<EnvironmentCheckResult> ValidateAsync(CancellationToken ct);
}