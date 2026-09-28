namespace Rask.Core.Browser;

/// <summary>A handle to one picked directory. Dispose to release the JS-side reference.</summary>
public interface IDirectoryHandle : IAsyncDisposable
{
    /// <summary>The directory name.</summary>
    string Name { get; }

    /// <summary>Lists the names of the directory's immediate entries (files and sub-directories).</summary>
    ValueTask<IReadOnlyList<string>> ListAsync();

    /// <summary>
    ///     Returns a handle to the file <paramref name="name" /> in this directory, optionally creating it
    ///     when <paramref name="create" /> is <c>true</c>.
    /// </summary>
    ValueTask<IFileHandle> GetFileAsync(string name, bool create = false);
}
