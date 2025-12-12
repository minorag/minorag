using Minorag.Cli.Indexing;

namespace Minorag.Cli.Services.Environments;

public class IgnoreRulesValidatorFactory(IFileSystemHelper fs)
{
    public IValidator Create(string workingDirectory)
    {
        return new IgnoreRulesValidator(fs, workingDirectory);
    }
}
