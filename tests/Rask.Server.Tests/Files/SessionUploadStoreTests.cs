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
        Assert.NotNull(entry);

        Assert.Equal("data.bin", entry!.Name);
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

        // Action writes nothing; the entry size falls back to the declared size.
        var entry = await store.StageAsync(
            "session-1", "empty.bin", "text/plain", 42, DateTimeOffset.UnixEpoch,
            _ => Task.CompletedTask);

        Assert.NotNull(entry);
        Assert.Equal(42, entry!.Size);
        store.Release("session-1", entry.Token);
    }

    [Fact]
    public async Task Quota_RejectsTheFileThatWouldExceedTheCumulativeCap()
    {
        using var store = new SessionUploadStore();
        const long quota = 100;

        // 60 staged (under quota → non-null).
        var a = await Stage(store, "s1", 60, quota);
        Assert.NotNull(a);

        // Another 60 would total 120 > 100 → rejected (null), nothing recorded.
        Assert.Null(await Stage(store, "s1", 60, quota));

        // A 40-byte one (→ exactly 100) still fits.
        var b = await Stage(store, "s1", 40, quota);
        Assert.NotNull(b);

        // Per-session budget: a different session has its own.
        Assert.NotNull(await Stage(store, "s2", 90, quota));

        // Releasing frees bytes back: s1 is full (100 = 60 + 40); release the 60, leaving 40, so a
        // 60-byte file now fits again (40 + 60 = 100) — but a 70 would not.
        store.Release("s1", a!.Token);
        Assert.Null(await Stage(store, "s1", 70, quota));
        Assert.NotNull(await Stage(store, "s1", 60, quota));
    }

    [Fact]
    public async Task ReleaseSession_ResetsTheQuotaTotal()
    {
        using var store = new SessionUploadStore();
        Assert.NotNull(await Stage(store, "s1", 80, 100));
        Assert.Null(await Stage(store, "s1", 80, 100)); // 160 > 100

        store.ReleaseSession("s1");
        Assert.NotNull(await Stage(store, "s1", 80, 100)); // budget reset
    }

    [Fact]
    public async Task Quota_NonPositive_NeverRejects()
    {
        using var store = new SessionUploadStore();
        Assert.NotNull(await Stage(store, "s1", 10 * 1024 * 1024, 0));
        Assert.NotNull(await Stage(store, "s1", 10 * 1024 * 1024, 0));
    }

    private static Task<SessionUploadStore.Entry?> Stage(
        SessionUploadStore store, string sessionId, int size, long quota) =>
        store.StageAsync(
            sessionId, $"f{size}.bin", "application/octet-stream", size, DateTimeOffset.UnixEpoch,
            path => File.WriteAllBytesAsync(path, new byte[size]), quota);
}
