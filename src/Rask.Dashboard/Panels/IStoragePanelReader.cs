namespace Rask.Dashboard.Panels;

/// <summary>
/// The stored-file reader, without the context type parameter — pages aren't generic, so they resolve this.
/// </summary>
public interface IStoragePanelReader
{
    /// <summary><c>false</c> when Rask.Storage isn't registered or its table isn't mapped.</summary>
    bool IsAvailable { get; }

    /// <summary>File count, stored bytes, how many are public, usage per provider, and the sweep's settings.</summary>
    Task<StorageStats> Stats(CancellationToken cancellationToken);

    /// <summary>One page of files, newest first, optionally filtered by a substring of the name.</summary>
    Task<(IReadOnlyList<StoredFileRow> Rows, int Total)> Page(
        string? search, int skip, int take, CancellationToken cancellationToken);
}
