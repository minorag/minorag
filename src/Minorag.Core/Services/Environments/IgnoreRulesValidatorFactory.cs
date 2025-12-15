namespace Minorag.Core.Services.Environments;

public class IgnoreRulesValidatorFactory(IFileSystemHelper fs)
{
    public IValidator Create(string workingDirectory)
    {
        return new IgnoreRulesValidator(fs, workingDirectory);
    }
}
