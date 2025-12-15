namespace Minorag.Core.Services;

public interface IFileSystemHelper
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Task<string[]> ReadAllLinesAsync(string path, CancellationToken ct);
    Task<string> ReadAllTextAsync(string path, CancellationToken ct);
    void CreateDirectory(string path);
    string[] GetDirectories(string directory);
    string[] GetFiles(string directory);
}

public class FileSystemHelper : IFileSystemHelper
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string[] GetDirectories(string directory) => Directory.GetDirectories(directory);
    public string[] GetFiles(string directory) => Directory.GetFiles(directory);
    public bool FileExists(string path) => File.Exists(path);
    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken ct) => File.ReadAllLinesAsync(path, ct);
    public Task<string> ReadAllTextAsync(string path, CancellationToken ct) => File.ReadAllTextAsync(path, ct);
}
