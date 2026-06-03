using Rask.Core.Routing;

namespace Rask.Example.Shared.Tests.Infrastructure;

internal sealed class CapturingDownloadSink : IDownloadSink
{
    public List<(string Filename, byte[] Bytes, string? ContentType)> Captured { get; } = [];

    public void Stage(string filename, byte[] bytes, string? contentType) =>
        Captured.Add((filename, bytes, contentType));

    public void Stage(string filename, Stream stream, string? contentType)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        Captured.Add((filename, ms.ToArray(), contentType));
    }

    public bool TryConsume(out PendingDownload? download)
    {
        download = null;
        return false;
    }
}
