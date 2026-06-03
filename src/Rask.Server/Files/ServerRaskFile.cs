using Rask.Core.Forms;

namespace Rask.Server.Files;

internal sealed class ServerRaskFile : RaskFile
{
    private readonly SessionUploadStore.Entry _entry;
    private readonly SessionUploadStore _store;

    public ServerRaskFile(SessionUploadStore.Entry entry, SessionUploadStore store)
    {
        _entry = entry;
        _store = store;
    }

    public override string Name => _entry.Name;
    public override long Size => _entry.Size;
    public override string ContentType => _entry.ContentType;
    public override DateTimeOffset LastModified => _entry.LastModified;

    internal string Token => _entry.Token;
    internal string SessionId => _entry.SessionId;

    public override Stream OpenReadStream(long maxAllowedSize = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (_entry.Size > maxAllowedSize)
        {
            throw new IOException(
                $"File '{_entry.Name}' is {_entry.Size} bytes, exceeds maxAllowedSize of {maxAllowedSize}.");
        }

        var fresh = _store.Get(_entry.SessionId, _entry.Token)
                    ?? throw new InvalidOperationException(
                        $"Upload token '{_entry.Token}' is no longer staged. " +
                        "OpenReadStream must be called from inside the handler that received the file.");
        return File.OpenRead(fresh.Path);
    }
}
