using Rask.Server.Files;

namespace Rask.Server.Tests.Files;

/// <summary>What a staged upload or download leaves on disk, and for whom.</summary>
public sealed class StagedFileTests
{
    [Fact]
    public void A_staged_download_is_readable_by_this_user_alone()
    {
        using var store = new SessionDownloadStore();
        using var source = new MemoryStream([1, 2, 3]);

        var entry = store.StageStream("session-1", "report.bin", source, null);
        var mode = OperatingSystem.IsWindows() ? (UnixFileMode?)null : File.GetUnixFileMode(entry.TempPath!);
        SessionDownloadStore.Release(entry);

        Assert.True(mode is null or (UnixFileMode.UserRead | UnixFileMode.UserWrite), $"mode {mode}");
    }

    [Fact]
    public void A_download_replaced_before_the_browser_asked_for_it_is_dropped()
    {
        using var store = new SessionDownloadStore();
        var sink = new ServerDownloadSink(store, new RaskSessionContext { Id = "session-1" });
        var before = Staged();
        using (var first = new MemoryStream([1]))
        {
            sink.Stage("first.bin", first, null);
        }

        var firstFile = Assert.Single(Staged().Except(before));

        sink.Stage("second.bin", [2], null);
        sink.TryConsume(out var download);

        Assert.Equal("second.bin", download!.Filename);
        Assert.False(File.Exists(firstFile));
    }

    private static HashSet<string> Staged() =>
        Directory.EnumerateFiles(Path.GetTempPath(), "rask-download-*.bin").ToHashSet(StringComparer.Ordinal);

    [Fact]
    public async Task An_upload_over_the_session_quota_is_refused_before_it_is_written()
    {
        using var store = new SessionUploadStore();
        var written = false;

        var entry = await store.StageAsync(
            "session-1", "big.bin", "application/octet-stream", size: 2_000, DateTimeOffset.UnixEpoch,
            _ =>
            {
                written = true;
                return Task.CompletedTask;
            },
            maxBytesPerSession: 1_000);

        Assert.Null(entry);
        Assert.False(written);
    }

    [Fact]
    public async Task An_upload_cut_off_partway_leaves_no_file_behind()
    {
        using var store = new SessionUploadStore();
        string? staged = null;

        await Assert.ThrowsAsync<IOException>(() => store.StageAsync(
            "session-1", "cut.bin", "application/octet-stream", size: 10, DateTimeOffset.UnixEpoch,
            async path =>
            {
                staged = path;
                await File.WriteAllBytesAsync(path, [1, 2, 3]);
                throw new IOException("the client went away");
            }));

        Assert.False(File.Exists(staged));
    }
}
