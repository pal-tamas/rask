using Rask.Server.Files;

namespace Rask.Server.Tests.Files;

public class SessionUploadStoreTests
{
    [Fact]
    public async Task StageAsync_AwaitsWriteCallback_BeforeRecordingEntry()
    {
        using var store = new SessionUploadStore();
        var gate = new TaskCompletionSource();
        var observedDuringWrite = (Entry: (SessionUploadStore.Entry?)null, Completed: false);

        var writeBytes = new byte[] { 1, 2, 3, 4 };
        var stageTask = store.StageAsync(
            "session-1", "data.bin", "application/octet-stream", writeBytes.Length, DateTimeOffset.UnixEpoch,
            async path =>
            {
                // The write must still be in flight here — StageAsync should not have
                // recorded the entry or returned yet (no sync-over-async block).
                await gate.Task.ConfigureAwait(false);
                await File.WriteAllBytesAsync(path, writeBytes).ConfigureAwait(false);
            });

        // StageAsync is genuinely async: it has not completed while the write is gated.
        observedDuringWrite.Completed = stageTask.IsCompleted;
        observedDuringWrite.Entry = store.Get("session-1", "ignored");
        Assert.False(observedDuringWrite.Completed);
        Assert.Null(observedDuringWrite.Entry);

        gate.SetResult();
        var entry = await stageTask;

        Assert.Equal("data.bin", entry.Name);
        Assert.Equal(writeBytes.Length, entry.Size);
        var staged = await File.ReadAllBytesAsync(entry.Path);
        Assert.Equal(writeBytes, staged);

        store.Release("session-1", entry.Token);
        Assert.False(File.Exists(entry.Path));
    }

    [Fact]
    public async Task StageAsync_FallsBackToProvidedSize_WhenFileMissing()
    {
        using var store = new SessionUploadStore();

        // Callback writes nothing; the entry size falls back to the declared size.
        var entry = await store.StageAsync(
            "session-1", "empty.bin", "text/plain", 42, DateTimeOffset.UnixEpoch,
            _ => Task.CompletedTask);

        Assert.Equal(42, entry.Size);
        store.Release("session-1", entry.Token);
    }

    [Fact]
    public async Task WouldExceedQuota_TracksCumulativeStagedBytesPerSession()
    {
        using var store = new SessionUploadStore();
        const long quota = 100;

        // Nothing staged yet — a 60-byte file fits.
        Assert.False(store.WouldExceedQuota("s1", 60, quota));
        var a = await store.StageAsync("s1", "a.bin", "application/octet-stream", 60, DateTimeOffset.UnixEpoch,
            path => File.WriteAllBytesAsync(path, new byte[60]));

        // 60 staged; another 60 would total 120 > 100 → rejected. A 40-byte one (→100) still fits.
        Assert.True(store.WouldExceedQuota("s1", 60, quota));
        Assert.False(store.WouldExceedQuota("s1", 40, quota));

        // The quota is per-session: a different session has its own budget.
        Assert.False(store.WouldExceedQuota("s2", 90, quota));

        // Releasing frees the bytes back to the session's budget.
        store.Release("s1", a.Token);
        Assert.False(store.WouldExceedQuota("s1", 90, quota));
    }

    [Fact]
    public async Task ReleaseSession_ResetsTheQuotaTotal()
    {
        using var store = new SessionUploadStore();
        await store.StageAsync("s1", "a.bin", "application/octet-stream", 80, DateTimeOffset.UnixEpoch,
            path => File.WriteAllBytesAsync(path, new byte[80]));
        Assert.True(store.WouldExceedQuota("s1", 80, 100));

        store.ReleaseSession("s1");
        Assert.False(store.WouldExceedQuota("s1", 80, 100));
    }

    [Fact]
    public void WouldExceedQuota_NonPositiveQuota_IsAlwaysAllowed()
    {
        using var store = new SessionUploadStore();
        Assert.False(store.WouldExceedQuota("s1", long.MaxValue, 0));
    }
}
