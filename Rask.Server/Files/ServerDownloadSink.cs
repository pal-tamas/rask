using Rask.Core.Routing;

namespace Rask.Server.Files;

internal sealed class ServerDownloadSink : IDownloadSink
{
    private readonly RaskSessionContext _session;
    private readonly SessionDownloadStore _store;
    private SessionDownloadStore.Entry? _pending;

    public ServerDownloadSink(SessionDownloadStore store, RaskSessionContext session)
    {
        _store = store;
        _session = session;
    }

    public void Stage(string filename, byte[] bytes, string? contentType)
        => _pending = _store.StageBytes(_session.Id, filename, bytes, contentType);

    public void Stage(string filename, Stream stream, string? contentType)
        => _pending = _store.StageStream(_session.Id, filename, stream, contentType);

    public bool TryConsume(out PendingDownload? download)
    {
        if (_pending is null)
        {
            download = null;
            return false;
        }

        var url = $"/_rask/download/{_pending.SessionId}/{_pending.Token}";
        download = new PendingDownload(_pending.Filename, _pending.ContentType, url, null);
        _pending = null;
        return true;
    }
}
