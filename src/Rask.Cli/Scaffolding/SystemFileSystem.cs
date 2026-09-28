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

    public void WriteSecretText(string path, string content)
    {
        // Created 0600 rather than written and then narrowed, so the secret is never readable by anybody
        // else, not even for the instant in between. An EXISTING file keeps the mode it was created with,
        // so it is narrowed afterwards as well.
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly.File;
        }

        using (var writer = new StreamWriter(path, options))
        {
            writer.Write(content);
        }

        OwnerOnly.Restrict(path, OwnerOnly.File);
    }

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
