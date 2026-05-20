namespace Rask.Core.Forms;

public abstract class RaskFile
{
    public abstract string Name { get; }
    public abstract long Size { get; }
    public abstract string ContentType { get; }
    public abstract DateTimeOffset LastModified { get; }

    public abstract Stream OpenReadStream(long maxAllowedSize = 512 * 1024,
        CancellationToken cancellationToken = default);
}
