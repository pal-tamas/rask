using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server.Files;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Files;

// A staged upload is held for the dispatch that was handed it and no longer: the per-session cap bounds what
// a tab has staged AT ONCE, not everything it has ever uploaded.
public partial class StagedUploadReleaseTests
{
    private const int Cap = 1000;
    private const int FileSize = 400;

    [Fact]
    public async Task A_tab_uploading_files_one_after_another_never_runs_out_of_its_upload_budget()
    {
        await using var tab = await Tab.OpenAsync();
        var statuses = new List<HttpStatusCode>();
        var frames = new List<string>();

        for (var i = 0; i < 6; i++)
        {
            var (status, file) = await tab.UploadAsync($"provider-{i}.csv");
            statuses.Add(status);
            if (file is not null)
            {
                frames.AddRange(await tab.HandAsync(tab.Reads, file.Value));
            }
        }

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Contains(frames, f => f.Contains($"read={6 * FileSize};batches=6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tab_submitting_a_form_with_a_file_again_and_again_never_runs_out_of_its_upload_budget()
    {
        await using var tab = await Tab.OpenAsync();
        var statuses = new List<HttpStatusCode>();
        var frames = new List<string>();

        for (var i = 0; i < 6; i++)
        {
            var (status, file) = await tab.UploadAsync($"provider-{i}.csv");
            statuses.Add(status);
            if (file is not null)
            {
                frames.AddRange(await tab.SubmitAsync(file.Value));
            }
        }

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Contains(frames, f => f.Contains($"read={6 * FileSize};batches=6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_file_its_handler_never_read_is_released_when_the_handler_returns()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("ignored.csv");
        var path = tab.StagedPath(file!.Value);

        var frames = await tab.HandAsync(tab.Ignores, file.Value);

        Assert.Contains(frames, f => f.Contains("batches=1", StringComparison.Ordinal));
        Assert.False(tab.IsStaged(file.Value));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_files_handler_that_takes_no_argument_runs_and_its_file_is_released()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("counted.csv");

        var frames = await tab.HandAsync(tab.Counts, file!.Value);

        Assert.Contains(frames, f => f.Contains("batches=1", StringComparison.Ordinal));
        Assert.False(tab.IsStaged(file.Value));
    }

    [Fact]
    public async Task A_queued_batch_whose_socket_closed_before_it_ran_releases_its_file_while_the_session_lives()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("queued.csv");
        await tab.HoldTheLockAsync();
        await tab.QueueBatchAsync(tab.Reads, file!.Value);

        await tab.CloseSocketAsync();
        tab.Gate.Open.TrySetResult();
        await Rask.TestSupport.WaitFor.True(() => !tab.IsStaged(file.Value), "the queued batch hands its file back");

        Assert.True(tab.SessionLives);
    }

    [Fact]
    public async Task A_file_whose_handler_threw_is_released()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("malformed.csv");
        var path = tab.StagedPath(file!.Value);

        await tab.HandAsync(tab.Throws, file.Value);

        Assert.False(tab.IsStaged(file.Value));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_file_sent_to_a_handler_that_is_gone_is_released()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("stale.csv");
        var path = tab.StagedPath(file!.Value);

        await tab.HandAsync("h999", file.Value);

        Assert.False(tab.IsStaged(file.Value));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_file_read_after_its_handler_finished_says_it_is_no_longer_staged()
    {
        await using var tab = await Tab.OpenAsync();
        var (_, file) = await tab.UploadAsync("late.csv");
        var late = new ServerRaskFile(tab.Store.Get(tab.SessionId, Tab.TokenOf(file!.Value))!, tab.Store);

        await tab.HandAsync(tab.Ignores, file.Value);
        var error = Assert.Throws<InvalidOperationException>(
            () => late.OpenReadStream(long.MaxValue, TestContext.Current.CancellationToken));

        Assert.Contains("is no longer staged", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uploads_staged_at_once_over_the_cap_are_still_refused()
    {
        await using var tab = await Tab.OpenAsync();

        var first = await tab.UploadAsync("a.csv");
        var second = await tab.UploadAsync("b.csv");
        var third = await tab.UploadAsync("c.csv");

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, third.Status);
    }

    [Fact]
    public async Task Two_uploads_in_flight_together_over_the_cap_cannot_both_be_staged()
    {
        await using var tab = await Tab.OpenAsync();

        var both = await Task.WhenAll(tab.UploadAsync("a.csv", 600), tab.UploadAsync("b.csv", 600));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.RequestEntityTooLarge],
            both.Select(r => r.Status).Order());
    }

    private sealed partial class Tab : IAsyncDisposable
    {
        private readonly RaskTestHost _host;
        private readonly WebSocket _ws;
        private readonly string _loader;
        private readonly string _hold;

        private Tab(RaskTestHost host, WebSocket ws, string sessionId, string html, FileReceivingGate gate)
        {
            _host = host;
            _ws = ws;
            Gate = gate;
            SessionId = sessionId;
            Reads = HandlerFor(html, "reads");
            Ignores = HandlerFor(html, "ignores");
            Counts = HandlerFor(html, "counts");
            Throws = HandlerFor(html, "throws");
            _loader = LoaderHandler().Match(html).Groups["handler"].Value;
            _hold = HoldHandler().Match(html).Groups["handler"].Value;
        }

        public FileReceivingGate Gate { get; }
        public string SessionId { get; }
        public string Reads { get; }
        public string Ignores { get; }
        public string Counts { get; }
        public string Throws { get; }
        public SessionUploadStore Store => _host.Server.Services.GetRequiredService<SessionUploadStore>();
        public bool SessionLives => _host.Store.Peek(SessionId) is not null;

        public static async Task<Tab> OpenAsync()
        {
            var gate = new FileReceivingGate();
            var host = RaskTestHost.Create<FileReceivingApp>(
                configureServices: s => s.AddSingleton(gate), diffMode: LiveDiffMode.DisabledFull);
            host.Server.Services.GetRequiredService<RaskUploadOptions>().MaxBytesPerSession = Cap;
            var html = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            return new Tab(host, ws, sessionId, html, gate);
        }

        public static string TokenOf(JsonElement file) => file.GetProperty("token").GetString()!;

        public string StagedPath(JsonElement file) => Store.Get(SessionId, TokenOf(file))!.Path;

        public bool IsStaged(JsonElement file) => Store.Get(SessionId, TokenOf(file)) is not null;

        public async Task<(HttpStatusCode Status, JsonElement? File)> UploadAsync(string name, int size = FileSize)
        {
            var content = new ByteArrayContent(new byte[size]);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            using var form = new MultipartFormDataContent { { content, "f0", name } };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/upload") { Content = form };
            request.Headers.Add("Rask-Session", SessionId);
            var response = await _host.Http.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return (response.StatusCode, null);
            }

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (response.StatusCode, body.RootElement.GetProperty("files")[0].Clone());
        }

        public async Task<List<string>> HandAsync(string handler, JsonElement file)
        {
            await _ws.SendJsonAsync(new { id = handler, type = "files", files = new[] { file } });
            return await _ws.SettledAsync();
        }

        public async Task<List<string>> SubmitAsync(JsonElement file)
        {
            await _ws.SendJsonAsync(new
            {
                id = _loader,
                type = "submit",
                form = new Dictionary<string, object>
                {
                    ["__files"] = new Dictionary<string, JsonElement[]> { ["doc"] = [file] }
                }
            });
            return await _ws.SettledAsync();
        }

        // Parks the hold button's handler on the gate, so whatever is sent next waits for the session's lock.
        public async Task HoldTheLockAsync()
        {
            await _ws.SendJsonAsync(new { id = _hold, type = "click" });
            await Gate.Parked.Task.WaitAsync(LiveFrames.HangCeiling);
        }

        public Task QueueBatchAsync(string handler, JsonElement file) =>
            _ws.SendJsonAsync(new
            {
                type = "batch",
                events = new[] { new { id = handler, type = "files", files = new[] { file } } }
            });

        public Task CloseSocketAsync() =>
            _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "tab hidden", CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            Gate.Open.TrySetResult();
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }

            _ws.Dispose();
            _host.Dispose();
        }

        private static string HandlerFor(string html, string inputId)
        {
            var match = InputHandler().Matches(html).FirstOrDefault(m => m.Groups["id"].Value == inputId);
            Assert.NotNull(match);
            return match!.Groups["handler"].Value;
        }

        [GeneratedRegex("<input[^>]*id=\"(?<id>[^\"]+)\"[^>]*data-rask-on-files=\"(?<handler>[^\"]+)\"")]
        private static partial Regex InputHandler();

        [GeneratedRegex("<form[^>]*data-rask-on-submit=\"(?<handler>[^\"]+)\"")]
        private static partial Regex LoaderHandler();

        [GeneratedRegex("<button[^>]*data-rask-on-click=\"(?<handler>[^\"]+)\"[^>]*>hold<")]
        private static partial Regex HoldHandler();
    }
}
