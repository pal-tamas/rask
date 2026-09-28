namespace Rask.Core.Forms;

public interface IRaskFile
{
    string Name { get; }
    long Size { get; }
    string ContentType { get; }
    DateTimeOffset LastModified { get; }

    Stream OpenReadStream(long maxAllowedSize = 512 * 1024,
        CancellationToken cancellationToken = default);
}
