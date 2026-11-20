namespace Rask.Cli.Scaffolding;

/// <summary>The real filesystem, backed by <see cref="File"/> / <see cref="Directory"/>.</summary>
internal sealed class SystemFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Tidying, not correctness.
        }
    }

    public void ReplaceDirectory(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.Move(source, destination);
    }

    public IReadOnlyList<string> ListFiles(string directory, string searchPattern) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            : [];

    public IReadOnlyList<string> ListFilesRecursive(string directory, string searchPattern) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories)
            : [];

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

    public void WriteSecretText(string path, string content) => OwnerOnly.WriteText(path, content);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }
}
