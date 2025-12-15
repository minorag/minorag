using Minorag.Core.Store;

namespace Minorag.Core.Services.Environments;

public class DatabaseValidatorFactory(RagDbContext db, IFileSystemHelper fs)
{
    public IValidator Create(string path)
    {
        return new DatabaseValidator(db, fs, path);
    }
}
