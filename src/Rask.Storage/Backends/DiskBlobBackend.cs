using System.Diagnostics.CodeAnalysis;

namespace Rask.Storage.Backends;

/// <summary>
/// Files in a directory — <c>/data/files</c> on the deploy volume by default.
/// </summary>
/// <remarks>
/// <para>
/// Uploads are spooled into <c>.tmp/</c> under the root, on the same volume as the files, so storing one is a
/// rename: a reader sees nothing or the whole file, never a half-written one, and nothing is copied twice.
/// The rename refuses to overwrite, because two files can never share an id.
/// </para>
/// <para>
/// A key that would leave the root is refused rather than normalised. Keys are generated from ids, so this
/// should never fire — which is exactly why it throws instead of quietly reading something else.
/// </para>
/// </remarks>
internal sealed class DiskBlobBackend : IBlobBackend
{
    internal const string SpoolFolder = ".tmp";

    private readonly string _root;
    private readonly string _boundary;

    internal DiskBlobBackend(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _boundary = _root + Path.DirectorySeparatorChar;
    }

    internal string Root => _root;

    public StorageProvider Provider => StorageProvider.Disk;

    public long MaxSinglePutBytes => long.MaxValue;

    public string CreateSpoolPath()
    {
        var spool = Path.Combine(_root, SpoolFolder);
        try
        {
            Directory.CreateDirectory(spool);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Rask.Storage cannot create its directory '{_root}': {ex.Message} Point Storage__Disk__Root at a "
                + "directory the app can write to.", ex);
        }

        return Path.Combine(spool, Guid.NewGuid().ToString("N") + ".tmp");
    }

    public Task PutFileAsync(string key, string sourcePath, long length, BlobHeaders headers, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(sourcePath, path, overwrite: false);
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string key, long offset, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        try
        {
            // FileShare.Delete: a file being downloaded can still be deleted; the reader keeps its handle.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 0, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (offset > 0)
            {
                stream.Seek(offset, SeekOrigin.Begin);
            }

            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            File.Delete(Resolve(key));
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone, with its directory.
        }

        return Task.CompletedTask;
    }

    public IAsyncEnumerable<BlobEntry> ListAsync(string prefix, CancellationToken cancellationToken) =>
        Enumerate(prefix, cancellationToken).ToAsyncEnumerable();

    public bool TryPresign(string key, TimeSpan lifetime, string contentType, string contentDisposition,
        [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        return false;
    }

    public Task DeleteStaleSpoolAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var spool = new DirectoryInfo(Path.Combine(_root, SpoolFolder));
        if (!spool.Exists)
        {
            return Task.CompletedTask;
        }

        foreach (var file in spool.EnumerateFiles("*.tmp"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc < olderThan.UtcDateTime)
            {
                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                    // Still open by a save in flight after all; the next sweep gets it.
                }
            }
        }

        return Task.CompletedTask;
    }

    private IEnumerable<BlobEntry> Enumerate(string prefix, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // A link inside the root could point anywhere; the listing only reports what is really here.
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var spool = Path.Combine(_root, SpoolFolder) + Path.DirectorySeparatorChar;
        foreach (var path in Directory.EnumerateFiles(_root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.StartsWith(spool, StringComparison.Ordinal))
            {
                continue;
            }

            var key = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var info = new FileInfo(path);
            if (info.Exists)
            {
                yield return new BlobEntry(key, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            }
        }
    }

    private string Resolve(string key)
    {
        if (!KeyLayout.IsValid(key) || Path.IsPathRooted(key))
        {
            throw new ArgumentException($"'{key}' is not a storage key.", nameof(key));
        }

        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_boundary, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{key}' resolves outside the storage directory.", nameof(key));
        }

        return full;
    }
}
