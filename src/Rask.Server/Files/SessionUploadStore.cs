using System.Collections.Concurrent;

namespace Rask.Server.Files;

internal sealed class SessionUploadStore : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    // Running total of staged bytes per session, for the optional per-session upload quota. Guarded by
    // its own lock so the quota check + reserve in StageAsync is atomic (concurrent same-session uploads
    // can't both pass a stale check and overshoot) and a release can't resurrect a removed session key.
    private readonly object _quotaLock = new();
    private readonly Dictionary<string, long> _sessionBytes = new(StringComparer.Ordinal);

    public void Dispose()
    {
        foreach (var key in _entries.Keys.ToArray())
        {
            if (_entries.TryRemove(key, out var entry))
            {
                TryDelete(entry.Path);
            }
        }

        lock (_quotaLock)
        {
            _sessionBytes.Clear();
        }
    }

    /// <summary>
    ///     Stages an uploaded file. When <paramref name="maxBytesPerSession" /> is positive and staging
    ///     this file would push the session's cumulative staged bytes over the quota, the just-written
    ///     temp file is deleted and <c>null</c> is returned (the caller answers <c>413</c>). The check
    ///     and the reserve are a single atomic step, so concurrent same-session uploads can't both pass a
    ///     stale check and overshoot the cap. Bytes are accounted by the actual written size, matching
    ///     what <see cref="Release" /> later frees.
    /// </summary>
    public async Task<Entry?> StageAsync(string sessionId, string name, string contentType, long size,
        DateTimeOffset lastModified, Func<string, Task> writeToPath, long maxBytesPerSession = 0)
    {
        var token = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Path.GetTempPath(), $"rask-upload-{token}.bin");
        await writeToPath(path).ConfigureAwait(false);
        var info = new FileInfo(path);
        var actualSize = info.Exists ? info.Length : size;

        if (maxBytesPerSession > 0)
        {
            lock (_quotaLock)
            {
                var current = _sessionBytes.GetValueOrDefault(sessionId);
                if (current + actualSize > maxBytesPerSession)
                {
                    // Over quota — drop the temp file and signal rejection without recording anything.
                    TryDelete(path);
                    return null;
                }

                _sessionBytes[sessionId] = current + actualSize;
            }
        }

        var entry = new Entry(sessionId, token, path, name, actualSize, contentType, lastModified);
        _entries[Key(sessionId, token)] = entry;
        return entry;
    }

    public Entry? Get(string sessionId, string token) =>
        _entries.TryGetValue(Key(sessionId, token), out var e) ? e : null;

    public void Release(string sessionId, string token)
    {
        if (_entries.TryRemove(Key(sessionId, token), out var entry))
        {
            ReleaseQuota(sessionId, entry.Size);
            TryDelete(entry.Path);
        }
    }

    public void ReleaseSession(string sessionId)
    {
        lock (_quotaLock)
        {
            _sessionBytes.Remove(sessionId);
        }

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

    // Decrement a session's staged-byte total, removing the key at zero. Only ever updates an existing
    // key — never inserts — so a Release racing just after ReleaseSession cleared the session can't
    // resurrect a phantom entry for the now-dead session.
    private void ReleaseQuota(string sessionId, long bytes)
    {
        lock (_quotaLock)
        {
            if (!_sessionBytes.TryGetValue(sessionId, out var current))
            {
                return;
            }

            var next = current - bytes;
            if (next <= 0)
            {
                _sessionBytes.Remove(sessionId);
            }
            else
            {
                _sessionBytes[sessionId] = next;
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
