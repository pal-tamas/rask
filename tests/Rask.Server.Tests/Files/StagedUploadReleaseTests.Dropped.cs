using System.Text.Json;
using Rask.Server.Tests.Infrastructure;
using Rask.TestSupport;

namespace Rask.Server.Tests.Files;

// A frame the server drops without dispatching it still hands back the files it named, while the session lives:
// kept, each would count against the session's upload budget until the tab closed, and enough of them answer every
// next upload 413.
public partial class StagedUploadReleaseTests
{
    [Fact]
    public async Task A_file_sent_while_a_sign_in_hands_the_tab_over_is_released_at_the_drop()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("during-sign-in.csv");
        var path = tab.StagedPath(file!.Value);
        tab.HandingOver();

        await tab.SendAsync(new { id = tab.Reads, type = "files", files = new[] { file.Value } });

        // Both, in one wait: the store forgets the token a moment before it deletes the file.
        await WaitFor.True(() => !tab.IsStaged(file.Value) && !File.Exists(path), "the dropped frame hands its file back");

        Assert.True(tab.SessionLives);
    }

    [Fact]
    public async Task A_batch_sent_while_a_sign_in_hands_the_tab_over_releases_its_files_at_the_drop()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("batched-during-sign-in.csv");
        tab.HandingOver();

        await tab.QueueBatchAsync(tab.Reads, file!.Value);

        await WaitFor.True(() => !tab.IsStaged(file.Value), "the dropped batch hands its file back");

        Assert.True(tab.SessionLives);
    }

    [Fact]
    public async Task A_form_submitted_while_a_sign_in_hands_the_tab_over_releases_its_file_at_the_drop()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("submitted-during-sign-in.csv");
        tab.HandingOver();

        await tab.SendAsync(tab.SubmitFrame(file!.Value));

        await WaitFor.True(() => !tab.IsStaged(file.Value), "the dropped submit hands its file back");

        Assert.True(tab.SessionLives);
    }

    [Fact]
    public async Task A_file_sent_in_a_frame_that_names_no_handler_is_released()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("no-handler.csv");

        await tab.SendAsync(new { type = "files", files = new[] { file!.Value } });

        await WaitFor.True(() => !tab.IsStaged(file.Value), "the unaddressed frame hands its file back");

        Assert.True(tab.SessionLives);
    }

    [Fact]
    public async Task A_file_sent_over_a_socket_another_tab_replaced_is_released()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("superseded.csv");
        await tab.ReplaceTheSocketAsync();

        await tab.SendAsync(new { id = tab.Reads, type = "files", files = new[] { file!.Value } });

        await WaitFor.True(() => !tab.IsStaged(file.Value), "the superseded socket's frame hands its file back");

        Assert.True(tab.SessionLives);
    }

    [Fact]
    public async Task A_file_refused_for_the_handler_backlog_is_released()
    {
        await using var tab = await Tab.OpenAsync(server: o => o.MaxPendingHandlers = 1);
        var (_, file) = await tab.UploadAsync("backlogged.csv");
        await tab.HoldTheLockAsync();

        await tab.SendAsync(new { id = tab.Reads, type = "files", files = new[] { file!.Value } });

        await WaitFor.True(() => !tab.IsStaged(file.Value), "the refused frame hands its file back");

        Assert.True(tab.SessionLives);
    }

    [Fact]
    public async Task A_batch_refused_for_the_handler_backlog_releases_its_files()
    {
        await using var tab = await Tab.OpenAsync(server: o => o.MaxPendingHandlers = 1);
        var (_, file) = await tab.UploadAsync("batch-backlogged.csv");
        await tab.HoldTheLockAsync();

        await tab.QueueBatchAsync(tab.Reads, file!.Value);

        await WaitFor.True(() => !tab.IsStaged(file.Value), "the refused batch hands its file back");

        Assert.True(tab.SessionLives);
    }

    [Fact]
    public async Task A_file_posted_behind_a_frame_the_handler_backlog_refused_is_released()
    {
        await using var tab = await Tab.OpenAsync(server: o => o.MaxPendingHandlers = 1);
        var (_, file) = await tab.UploadAsync("posted-behind.csv");
        await using var stream = await tab.SwitchToTheHttpStreamAsync();
        await tab.HoldTheLockAsync(stream);

        await stream.SendRawAsync(
            JsonSerializer.Serialize(new { id = tab.Hold, type = "click" }) + ","
            + JsonSerializer.Serialize(new { id = tab.Reads, type = "files", files = new[] { file!.Value } }));

        await WaitFor.True(() => !tab.IsStaged(file.Value), "the frame the refusal left undispatched hands its file back");

        Assert.True(tab.SessionLives);
    }

    private sealed partial class Tab
    {
        public string Hold => _hold;

        // The tab falls back to the HTTP transport: a stream that names the session, and frames POSTed in batches.
        public async Task<ILiveTestConnection> SwitchToTheHttpStreamAsync()
        {
            var session = _host.Store.Peek(SessionId)!;
            var settled = session.AttachesSettled;
            var stream = await LiveTestConnection.OpenAsync(_host, LiveTransportKind.Http, SessionId);
            await WaitFor.True(() => session.AttachesSettled > settled, "the stream is attached");
            return stream;
        }

        public async Task HoldTheLockAsync(ILiveTestConnection connection)
        {
            await connection.SendJsonAsync(new { id = _hold, type = "click" });
            await Gate.Parked.Task.WaitAsync(LiveFrames.HangCeiling);
        }

        // What a sign-in does once it has handed the browser to its redeem fetch and reconnect.
        public void HandingOver() => _host.Store.Peek(SessionId)!.SuppressEventsUntilReconnect = true;

        public Task SendAsync(object frame) => _ws.SendJsonAsync(frame);

        public object SubmitFrame(JsonElement file) => new
        {
            id = _loader,
            type = "submit",
            form = new Dictionary<string, object>
            {
                ["__files"] = new Dictionary<string, JsonElement[]> { ["doc"] = [file] }
            }
        };

        // The same tab reconnecting before the server noticed its first socket die: the first speaks for the
        // session no longer.
        public async Task ReplaceTheSocketAsync()
        {
            _successor = await _host.WebSockets.ConnectAsync(_host.WebSocketUri, CancellationToken.None);
            await _successor.SendJsonAsync(new { type = "hello", session = SessionId });
            await _successor.ReceiveTextAsync();
        }
    }
}
