using Rask.Core.Live;
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
        => Pending(_store.StageBytes(_session.Id, filename, bytes, contentType));

    public void Stage(string filename, Stream stream, string? contentType)
        => Pending(_store.StageStream(_session.Id, filename, stream, contentType));

    // One download per event reaches the browser, the last staged: one staged before it is dropped now, not held — its
    // bytes or its temp file — until the session ends.
    private void Pending(SessionDownloadStore.Entry entry)
    {
        if (_pending is { } replaced)
        {
            _store.Discard(replaced);
        }

        _pending = entry;
    }

    public bool TryConsume(out PendingDownload? download)
    {
        if (_pending is null)
        {
            download = null;
            return false;
        }

        // The token alone: the browser navigates to it, and a URL is written down by logs, history and Referer.
        var url = $"{LiveOptions.PathBase}/_rask/download/{_pending.Token}";
        download = new PendingDownload(_pending.Filename, _pending.ContentType, url);
        _pending = null;
        return true;
    }
}
