using System.Collections.Concurrent;

namespace Rask.Server.Files;

internal sealed class SessionDownloadStore : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public void Dispose()
    {
        foreach (var key in _entries.Keys.ToArray())
        {
            if (_entries.TryRemove(key, out var entry) && entry.TempPath is { } path)
            {
                TryDelete(path);
            }
        }
    }

    public Entry StageBytes(string sessionId, string filename, byte[] bytes, string? contentType)
    {
        var token = Guid.NewGuid().ToString("N");
        var entry = new Entry(sessionId, token, filename, contentType ?? "application/octet-stream", bytes, null);
        _entries[Key(sessionId, token)] = entry;
        return entry;
    }

    public Entry StageStream(string sessionId, string filename, Stream stream, string? contentType)
    {
        var token = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Path.GetTempPath(), $"rask-download-{token}.bin");
        // Eagerly copy to a temp file (synchronously) rather than holding the caller's
        // stream until the download HTTP request arrives. This decouples the file's
        // lifetime from the source stream — IDownloadSink.Stage / Navigator.Download are
        // synchronous, fire-and-forget APIs called from an event handler, so the stream
        // (often a `using` MemoryStream/FileStream) may be disposed the moment the handler
        // returns. Copying now is the price of that contract; keep it sync to avoid
        // turning the whole public Download path async.
        using (var f = File.Create(path))
        {
            stream.CopyTo(f);
        }

        var entry = new Entry(sessionId, token, filename, contentType ?? "application/octet-stream", null, path);
        _entries[Key(sessionId, token)] = entry;
        return entry;
    }

    public bool TryTake(string sessionId, string token, out Entry? entry)
    {
        if (_entries.TryRemove(Key(sessionId, token), out entry))
        {
            return true;
        }

        entry = null;
        return false;
    }

    public void Release(Entry entry)
    {
        if (entry.TempPath is { } path)
        {
            TryDelete(path);
        }
    }

    public void ReleaseSession(string sessionId)
    {
        foreach (var key in _entries.Keys.ToArray())
        {
            if (!key.StartsWith(sessionId + ":", StringComparison.Ordinal))
            {
                continue;
            }

            if (_entries.TryRemove(key, out var entry) && entry.TempPath is { } path)
            {
                TryDelete(path);
            }
        }
    }

    private static string Key(string sessionId, string token) => sessionId + ":" + token;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup; ignore
        }
    }

    public sealed record Entry(
        string SessionId,
        string Token,
        string Filename,
        string ContentType,
        byte[]? Bytes,
        string? TempPath);
}
