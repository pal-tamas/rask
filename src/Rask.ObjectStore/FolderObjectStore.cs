namespace Rask.ObjectStore;

/// <summary>
///     A bucket backed by a directory on disk.
/// </summary>
/// <remarks>
///     <para>
///         Useful in three places, and it is the same code for all of them: running a sample or a test
///         without any cloud credentials, a single-machine deployment that has no reason to pay for
///         object storage, and — the interesting one — a folder something <em>else</em> already
///         replicates. Point it at a Syncthing share and devices converge with no central server at all;
///         point it at iCloud Drive, Dropbox or OneDrive and the sync is somebody else's problem.
///     </para>
///     <para>
///         Keys map to relative paths, so a key containing <c>/</c> becomes a subdirectory. Anything that
///         would escape the root is refused rather than normalised: keys can come from a listing of a
///         shared folder, so they are not automatically this process's own strings.
///     </para>
/// </remarks>
public sealed class FolderObjectStore : IObjectStore
{
    private readonly string _root;

    /// <summary>Creates a store over <paramref name="root" />, creating the directory if needed.</summary>
    public FolderObjectStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetRangeAsync(
        string key, long offset, int count, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        if (offset >= stream.Length)
        {
            return [];
        }

        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[Math.Min(count, stream.Length - offset)];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    /// <inheritdoc />
    public async Task PutAsync(string key, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written beside and moved into place: a reader that lists the folder concurrently sees either
        // nothing or the whole object, never a half-written one. That matters most for the case this
        // exists for — a folder another process is replicating while it is being written.
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    /// <inheritdoc />
    public async Task PutAsync(
        string key, Stream content, long length, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".tmp";
        await using (var file = File.Create(temporary))
        {
            await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(
        string key, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            // CreateNew is the filesystem's own atomic "only if absent" — the same guarantee
            // If-None-Match gives over HTTP, which is what this method means.
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ObjectEntry>> ListAsync(
        string prefix, string? startAfter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var entries = Enumerate(prefix)
            .Where(key => startAfter is null || string.CompareOrdinal(key, startAfter) > 0)
            .Order(StringComparer.Ordinal)   // ordinal, because forward-only reading depends on it
            .Select(key =>
            {
                var info = new FileInfo(Resolve(key));
                return new ObjectEntry(key, info.Length, info.LastWriteTimeUtc, null);
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ObjectEntry>>(entries);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPrefixesAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var prefixes = Enumerate(prefix)
            .Select(key => key[prefix.Length..])
            .Select(rest => rest.IndexOf('/', StringComparison.Ordinal) is var slash && slash >= 0
                ? prefix + rest[..(slash + 1)]
                : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(prefixes);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        File.Delete(Resolve(key));
        return Task.CompletedTask;
    }

    /// <summary>Every key under <paramref name="prefix" />, in the store's own key form.</summary>
    private IEnumerable<string> Enumerate(string prefix)
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            // A half-written object, not yet moved into place.
            if (path.EndsWith(".tmp", StringComparison.Ordinal))
            {
                continue;
            }

            var key = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return key;
            }
        }
    }

    /// <summary>
    ///     The path a key names, refusing anything outside the root.
    /// </summary>
    /// <remarks>
    ///     Keys are not necessarily this process's own strings — a peer's prefix comes back from a
    ///     listing of a shared folder — so a key that climbs out of the root is refused rather than
    ///     normalised into something that happens to be readable.
    /// </remarks>
    private string Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (Path.IsPathRooted(key))
        {
            throw new ArgumentException($"'{key}' is an absolute path, not a key.", nameof(key));
        }

        var full = Path.GetFullPath(Path.Combine(_root, key));
        var boundary = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(boundary, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{key}' resolves outside the store's folder.", nameof(key));
        }

        return full;
    }
}
