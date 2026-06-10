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
}
