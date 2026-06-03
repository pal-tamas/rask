namespace Rask.Core.Routing;

public interface IDownloadSink
{
    void Stage(string filename, byte[] bytes, string? contentType);

    void Stage(string filename, Stream stream, string? contentType);

    bool TryConsume(out PendingDownload? download);
}
