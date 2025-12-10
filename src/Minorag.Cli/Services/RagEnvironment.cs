namespace Minorag.Cli.Services;

public static class RagEnvironment
{
    public static DirectoryInfo GetRepoRootOrCurrent()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        // fallback: current directory
        return new DirectoryInfo(Environment.CurrentDirectory);
    }

    public static string GetDefaultDbPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var ragDir = Path.Combine(home, ".minorag");
        Directory.CreateDirectory(ragDir);

        return Path.GetFullPath(Path.Combine(ragDir, "index.db"));
    }
}