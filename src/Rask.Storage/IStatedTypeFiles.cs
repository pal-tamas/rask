namespace Rask.Storage;

/// <summary>
/// The saves under <c>Save(…).As(contentType)</c>: <see cref="IFiles.Add(Stream, string, bool, CancellationToken)"/>
/// with the type the caller stated in place of the sniffed one. Internal, so <see cref="IFiles"/> keeps its shape; the
/// store and the fake implement it.
/// </summary>
internal interface IStatedTypeFiles
{
    Task<StoredFile> Add(
        Func<long, CancellationToken, Stream> openRead, string name, long size, bool isPublic, string contentType,
        CancellationToken cancellationToken);

    Task<StoredFile> Add(Stream content, string name, bool isPublic, string contentType, CancellationToken cancellationToken);
}
