using System.Collections.Concurrent;

namespace Rask.Server.Files;

/// <summary>
///     The files a session's handlers offered, each until its one request. Keyed by its token alone: the download URL
///     the browser navigates to carries that and nothing else, so the session id never lands in a URL. The entry
///     remembers its session, and the endpoint serves it only to that session's user.
/// </summary>
internal sealed class SessionDownloadStore : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

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
        _entries[token] = entry;
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
        _entries[token] = entry;
        return entry;
    }

    /// <summary>The session <paramref name="token" /> was staged for, without taking it.</summary>
    public string? SessionOf(string token) => _entries.TryGetValue(token, out var entry) ? entry.SessionId : null;

    /// <summary>Takes the entry, once, and only for the session it was staged for.</summary>
    public bool TryTake(string sessionId, string token, out Entry? entry)
    {
        if (_entries.TryGetValue(token, out var found)
            && string.Equals(found.SessionId, sessionId, StringComparison.Ordinal)
            && _entries.TryRemove(new KeyValuePair<string, Entry>(token, found)))
        {
            entry = found;
            return true;
        }

        entry = null;
        return false;
    }

    public static void Release(Entry entry)
    {
        if (entry.TempPath is { } path)
        {
            TryDelete(path);
        }
    }

    public void ReleaseSession(string sessionId)
    {
        foreach (var pair in _entries.ToArray())
        {
            if (string.Equals(pair.Value.SessionId, sessionId, StringComparison.Ordinal)
                && _entries.TryRemove(pair) && pair.Value.TempPath is { } path)
            {
                TryDelete(path);
            }
        }
    }

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
