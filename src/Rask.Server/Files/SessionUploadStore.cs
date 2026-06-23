using System.Collections.Concurrent;

namespace Rask.Server.Files;

internal sealed class SessionUploadStore : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    // Running total of staged bytes per session, for the optional per-session upload quota.
    private readonly ConcurrentDictionary<string, long> _sessionBytes = new();

    public void Dispose()
    {
        foreach (var key in _entries.Keys.ToArray())
        {
            if (_entries.TryRemove(key, out var entry))
            {
                TryDelete(entry.Path);
            }
        }

        _sessionBytes.Clear();
    }

    /// <summary>
    ///     True when staging <paramref name="incomingBytes" /> more bytes for <paramref name="sessionId" />
    ///     would push its cumulative staged total over <paramref name="maxBytesPerSession" /> (a
    ///     non-positive quota is always allowed). The upload endpoint calls this before staging and
    ///     answers <c>413</c> when it returns true.
    /// </summary>
    public bool WouldExceedQuota(string sessionId, long incomingBytes, long maxBytesPerSession) =>
        maxBytesPerSession > 0 && _sessionBytes.GetValueOrDefault(sessionId) + incomingBytes > maxBytesPerSession;

    public async Task<Entry> StageAsync(string sessionId, string name, string contentType, long size,
        DateTimeOffset lastModified, Func<string, Task> writeToPath)
    {
        var token = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Path.GetTempPath(), $"rask-upload-{token}.bin");
        await writeToPath(path).ConfigureAwait(false);
        var info = new FileInfo(path);
        var actualSize = info.Exists ? info.Length : size;
        var entry = new Entry(sessionId, token, path, name, actualSize, contentType, lastModified);
        _entries[Key(sessionId, token)] = entry;
        _sessionBytes.AddOrUpdate(sessionId, actualSize, (_, current) => current + actualSize);
        return entry;
    }

    public Entry? Get(string sessionId, string token) =>
        _entries.TryGetValue(Key(sessionId, token), out var e) ? e : null;

    public void Release(string sessionId, string token)
    {
        if (_entries.TryRemove(Key(sessionId, token), out var entry))
        {
            _sessionBytes.AddOrUpdate(sessionId, 0, (_, current) => current - entry.Size);
            TryDelete(entry.Path);
        }
    }

    public void ReleaseSession(string sessionId)
    {
        _sessionBytes.TryRemove(sessionId, out _);
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
