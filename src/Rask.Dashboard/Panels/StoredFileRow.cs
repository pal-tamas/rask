using Rask.Storage;

namespace Rask.Dashboard.Panels;

/// <summary>One stored file, as the console lists it.</summary>
/// <param name="Id">The file's id.</param>
/// <param name="Name">Its display name — text an uploader chose.</param>
/// <param name="ContentType">The sniffed media type.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Provider">Where its bytes are.</param>
/// <param name="Public">Whether anyone with the link may fetch it.</param>
/// <param name="CreatedAt">When it was saved (UTC).</param>
public sealed record StoredFileRow(
    Guid Id, string Name, string ContentType, long Size, StorageProvider Provider, bool Public, DateTime CreatedAt);
