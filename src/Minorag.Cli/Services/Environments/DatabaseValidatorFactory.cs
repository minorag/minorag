using Minorag.Cli.Indexing;
using Minorag.Cli.Store;

namespace Minorag.Cli.Services.Environments;

public class DatabaseValidatorFactory(RagDbContext db, IFileSystemHelper fs)
{
    public IValidator Create(string path)
    {
        return new DatabaseValidator(db, fs, path);
    }
}
