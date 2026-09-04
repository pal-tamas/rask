namespace Rask.Cli.Scaffolding;

/// <summary>
/// The filesystem seam the scaffolder writes through. Abstracting it keeps project detection and file
/// generation unit-testable — tests drive an in-memory implementation and never touch disk.
/// </summary>
internal interface IFileSystem
{
    bool FileExists(string path);

    /// <summary>Files directly in <paramref name="directory"/> matching <paramref name="searchPattern"/> (non-recursive).</summary>
    IReadOnlyList<string> ListFiles(string directory, string searchPattern);

    /// <summary>Files under <paramref name="directory"/> matching <paramref name="searchPattern"/>, recursively.</summary>
    IReadOnlyList<string> ListFilesRecursive(string directory, string searchPattern);

    string ReadAllText(string path);

    void CreateDirectory(string path);

    void WriteAllText(string path, string content);

    /// <summary>
    /// Delete <paramref name="path"/> if it's there, swallowing an I/O or permission failure. For temp
    /// files whose removal is hygiene rather than correctness — failing to tidy up must never fail the
    /// operation that already succeeded.
    /// </summary>
    void TryDelete(string path);

    /// <summary>Whether <paramref name="path"/> is an existing directory.</summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Delete <paramref name="path"/> and everything under it, swallowing an I/O or permission
    /// failure. Same reasoning as <see cref="TryDelete"/>: this is tidying, and failing to tidy must
    /// not fail an operation that already succeeded.
    /// </summary>
    void TryDeleteDirectory(string path);
}

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
