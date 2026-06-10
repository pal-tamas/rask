using System.Collections.Concurrent;

namespace Rask.Server.Files;

internal sealed class SessionUploadStore : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public void Dispose()
    {
        foreach (var key in _entries.Keys.ToArray())
        {
            if (_entries.TryRemove(key, out var entry))
            {
                TryDelete(entry.Path);
            }
        }
    }

    public async Task<Entry> StageAsync(string sessionId, string name, string contentType, long size,
        DateTimeOffset lastModified, Func<string, Task> writeToPath)
    {
        var token = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Path.GetTempPath(), $"rask-upload-{token}.bin");
        await writeToPath(path).ConfigureAwait(false);
        var info = new FileInfo(path);
        var entry = new Entry(sessionId, token, path, name, info.Exists ? info.Length : size, contentType,
            lastModified);
        _entries[Key(sessionId, token)] = entry;
        return entry;
    }

    public Entry? Get(string sessionId, string token) =>
        _entries.TryGetValue(Key(sessionId, token), out var e) ? e : null;

    public void Release(string sessionId, string token)
    {
        if (_entries.TryRemove(Key(sessionId, token), out var entry))
        {
            TryDelete(entry.Path);
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

            if (_entries.TryRemove(key, out var entry))
            {
                TryDelete(entry.Path);
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
        string Path,
        string Name,
        long Size,
        string ContentType,
        DateTimeOffset LastModified);
}
