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
    /// Write <paramref name="content"/> readable by its owner alone (<c>0600</c> on Unix). For the files a
    /// scaffold generates that hold a secret — a signing key — which the ordinary umask would leave
    /// world-readable on a shared machine.
    /// </summary>
    void WriteSecretText(string path, string content);

    /// <summary>
    /// Write <paramref name="bytes"/> verbatim. For the template files that are not text — a PNG and two
    /// .ico favicons the front-end creators ship — where <see cref="WriteAllText"/> would re-encode the
    /// content as UTF-8 and corrupt it.
    /// </summary>
    void WriteAllBytes(string path, byte[] bytes);

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
