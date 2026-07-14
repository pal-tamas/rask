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

    string ReadAllText(string path);

    void CreateDirectory(string path);

    void WriteAllText(string path, string content);
}

/// <summary>The real filesystem, backed by <see cref="File"/> / <see cref="Directory"/>.</summary>
internal sealed class SystemFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public IReadOnlyList<string> ListFiles(string directory, string searchPattern) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            : [];

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
}
