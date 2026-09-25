namespace Rask.Storage.Backends;

/// <summary>Removes the spool files a crash mid-save leaves behind.</summary>
internal static class SpoolCleanup
{
    internal static void DeleteOlderThan(string directory, DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var spool = new DirectoryInfo(directory);
        if (!spool.Exists)
        {
            return;
        }

        foreach (var file in spool.EnumerateFiles("*.tmp"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc >= olderThan.UtcDateTime)
            {
                continue;
            }

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
}
