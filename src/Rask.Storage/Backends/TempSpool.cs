namespace Rask.Storage.Backends;

/// <summary>
/// Spool files in a directory private to one backend, for the stores whose bytes are written somewhere else — a
/// bucket, the database — rather than renamed into place.
/// </summary>
/// <remarks>
/// Not a fixed <c>/tmp</c> path: a predictable shared folder can be created first by another local user, who then owns
/// it and could swap a vetted upload's bytes between the sniff and the upload.
/// <see cref="Directory.CreateTempSubdirectory"/> makes a fresh one, owner-only on Unix.
/// </remarks>
internal sealed class TempSpool
{
    private readonly Lock _gate = new();
    private string? _directory;

    /// <summary>A fresh path, not yet created, in the spool directory.</summary>
    public string CreatePath()
    {
        lock (_gate)
        {
            if (_directory is null || !Directory.Exists(_directory))
            {
                _directory = Directory.CreateTempSubdirectory("rask-storage-").FullName;
            }

            return Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
        }
    }

    /// <summary>Removes spool files older than <paramref name="olderThan"/>.</summary>
    public void DeleteOlderThan(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        if (_directory is { } directory)
        {
            SpoolCleanup.DeleteOlderThan(directory, olderThan, cancellationToken);
        }
    }

    /// <summary>Opens a spooled file to be read once, front to back.</summary>
    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 0,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
}
