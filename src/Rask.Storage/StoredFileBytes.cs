namespace Rask.Storage;

/// <summary>
/// The bytes of a file kept on <see cref="StorageProvider.Database"/>, one row per <see cref="StoredFile"/> under the
/// same id.
/// </summary>
/// <remarks>
/// A table of its own rather than a column on <see cref="StoredFile"/>, so listing files, the operator console and the
/// orphan sweep never read a byte of them. Written and read as a stream (<c>DatabaseBlobBackend</c>), never through
/// this class, which exists so the model — and an app's migrations — know the table.
/// </remarks>
internal sealed class StoredFileBytes
{
    /// <summary>The id of the <see cref="StoredFile"/> these bytes belong to.</summary>
    public Guid Id { get; set; }

    /// <summary>The file's content.</summary>
    public byte[] Bytes { get; set; } = [];
}
