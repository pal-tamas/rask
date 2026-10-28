using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Server.Files;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

public class UploadDownloadEndpointTests
{
    [Fact]
    public async Task An_upload_to_an_unknown_session_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var form = BuildSingleFileForm("hi.txt", new byte[] { 1, 2, 3 });

        var response = await UploadAsync(host, "no-such-session", form);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_multipart_upload_stages_the_file_and_returns_its_token()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);
        var form = BuildSingleFileForm("data.bin", new byte[] { 9, 8, 7, 6, 5 });

        var response = await UploadAsync(host, sessionId, form);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var files = doc.RootElement.GetProperty("files");
        Assert.Equal(1, files.GetArrayLength());
        var f = files[0];
        Assert.Equal("data.bin", f.GetProperty("name").GetString());
        Assert.Equal(5, f.GetProperty("size").GetInt64());

        var store = host.Server.Services.GetRequiredService<SessionUploadStore>();
        var entry = store.Get(sessionId, f.GetProperty("token").GetString()!);
        Assert.NotNull(entry);
        var staged = await File.ReadAllBytesAsync(entry!.Path, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 9, 8, 7, 6, 5 }, staged);
    }

    [Fact]
    public async Task A_known_download_token_returns_the_bytes_as_an_attachment_exactly_once()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);

        var store = host.Server.Services.GetRequiredService<SessionDownloadStore>();
        var bytes = Encoding.UTF8.GetBytes("rask-report");
        var entry = store.StageBytes(sessionId, "report.txt", bytes, "text/plain");

        var url = $"/_rask/download/{entry.Token}";
        var first = await host.Http.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var content = await first.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bytes, content);
        Assert.Equal("text/plain", first.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", first.Content.Headers.ContentDisposition?.ToString() ?? "");
        // The staged content-type is attacker-influenceable (echoed from the upload), so the
        // download must forbid MIME sniffing alongside forcing an attachment download.
        Assert.True(first.Headers.TryGetValues("X-Content-Type-Options", out var nosniff));
        Assert.Equal("nosniff", nosniff.Single());

        // One-shot: a second fetch returns 404.
        var second = await host.Http.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task An_unknown_download_token_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/_rask/download/nope", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_upload_without_a_session_header_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.PostAsync(
            "/_rask/upload", BuildSingleFileForm("a.txt", [1]), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // What a page offers to download is a URL the browser navigates to, and a URL is written down by request logs,
    // history and Referer: it carries the one-shot token, never the session id.
    [Fact]
    public async Task The_url_a_handler_offers_to_download_carries_no_session_id()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var session = host.Store.Create(sp => new TestApp(sp.GetRequiredService<RouteState>()));
        var sink = session.Services.GetRequiredService<IDownloadSink>();
        sink.Stage("report.txt", [1, 2, 3], "text/plain");

        Assert.True(sink.TryConsume(out var download));

        Assert.DoesNotContain(session.Id, download!.Url, StringComparison.Ordinal);
        Assert.Matches("^/_rask/download/[0-9a-f]{32}$", download.Url);
        var response = await host.Http.GetAsync(download.Url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A tab opened before the header still names its session in the path, and is answered until the next release.
    [Fact]
    public async Task A_runtime_from_before_still_uploads_and_downloads_with_the_session_in_the_path()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);
        var entry = host.Server.Services.GetRequiredService<SessionDownloadStore>()
            .StageBytes(sessionId, "report.txt", Encoding.UTF8.GetBytes("rask-report"), "text/plain");

        var upload = await host.Http.PostAsync(
            "/_rask/upload/" + sessionId, BuildSingleFileForm("a.txt", [1]), TestContext.Current.CancellationToken);
        var download = await host.Http.GetAsync($"/_rask/download/{sessionId}/{entry.Token}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    // The token is bound to the session it was staged for: naming another session in the old path takes nothing.
    [Fact]
    public async Task A_download_token_is_not_served_under_another_session_s_path()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var mine = await CreateSessionAsync(host);
        var other = await CreateSessionAsync(host);
        var entry = host.Server.Services.GetRequiredService<SessionDownloadStore>()
            .StageBytes(mine, "report.txt", Encoding.UTF8.GetBytes("secret"), "text/plain");

        var stolen = await host.Http.GetAsync($"/_rask/download/{other}/{entry.Token}", TestContext.Current.CancellationToken);
        var own = await host.Http.GetAsync($"/_rask/download/{entry.Token}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    // A page that was fetched and never connected is reclaimed after its grace period. A download or upload
    // naming its id used to cancel that removal for good, so one request pinned a session — and its staged
    // files — in memory until the process ended.
    [Fact]
    public async Task A_download_request_does_not_keep_a_never_connected_session_alive()
    {
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => o.UnconnectedSessionGracePeriod = TimeSpan.FromMilliseconds(50));
        var sessionId = await CreateSessionAsync(host);
        var store = host.Server.Services.GetRequiredService<LiveSessionStore>();

        await host.Http.GetAsync($"/_rask/download/{sessionId}/nope", TestContext.Current.CancellationToken);
        await UploadAsync(host, sessionId, BuildSingleFileForm("a.txt", [1]));

        await WaitFor.True(() => store.Peek(sessionId) is null, "the session to be removed");

        Assert.Null(store.Peek(sessionId));
    }

    // The store can still hand out a session that its removal is tearing down on another thread: the grace period
    // above ending between the endpoint's lookup and its owner check. That request finds the session gone, as
    // one a moment later would, instead of failing on its disposed services (#1207). Here the session's scope
    // is disposed while the store still holds it, so the window the timing only sometimes opens is open every
    // run — the scope alone, as the session itself is the host's to dispose, once.
    [Fact]
    public async Task A_download_for_a_session_disposed_after_its_lookup_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var session = host.Store.Create(sp => new TestApp(sp.GetRequiredService<RouteState>()));
        session.Scope.Dispose();

        var entry = host.Server.Services.GetRequiredService<SessionDownloadStore>()
            .StageBytes(session.Id, "report.txt", [1], "text/plain");

        var response = await host.Http.GetAsync($"/_rask/download/{entry.Token}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_upload_to_a_session_disposed_after_its_lookup_answers_404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var session = host.Store.Create(sp => new TestApp(sp.GetRequiredService<RouteState>()));
        session.Scope.Dispose();

        var response = await UploadAsync(host, session.Id, BuildSingleFileForm("a.txt", [1]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_upload_filename_with_a_path_comes_back_as_the_sanitized_leaf_name()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);
        var form = BuildSingleFileForm("../../etc/passwd", new byte[] { 1, 2, 3 });

        var response = await UploadAsync(host, sessionId, form);

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var name = doc.RootElement.GetProperty("files")[0].GetProperty("name").GetString();
        // The directory components are stripped before the name is stored and echoed, so a host
        // that surfaces it cannot be steered into a traversal (and it must still HTML-encode it).
        Assert.Equal("passwd", name);
    }

    [Fact]
    public async Task A_cross_origin_upload_is_rejected()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);

        var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/upload")
        {
            Content = BuildSingleFileForm("data.bin", new byte[] { 1, 2, 3 })
        };
        request.Headers.Add("Rask-Session", sessionId);
        request.Headers.Add("Origin", "http://evil.example");

        var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_cross_origin_download_is_rejected()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);

        var store = host.Server.Services.GetRequiredService<SessionDownloadStore>();
        var entry = store.StageBytes(sessionId, "report.txt", Encoding.UTF8.GetBytes("secret"), "text/plain");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/_rask/download/{entry.Token}");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // The cross-origin attempt must not consume the one-shot entry — a legitimate same-origin
        // fetch still succeeds afterward.
        var legit = await host.Http.GetAsync($"/_rask/download/{entry.Token}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, legit.StatusCode);
    }

    // As the runtime uploads: the session in the Rask-Session header, never in the path.
    private static async Task<HttpResponseMessage> UploadAsync(RaskTestHost host, string sessionId, MultipartFormDataContent form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/upload") { Content = form };
        request.Headers.Add("Rask-Session", sessionId);
        return await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> CreateSessionAsync(RaskTestHost host)
    {
        var response = await host.Http.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var marker = "data-rask-root=\"";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        var start = idx + marker.Length;
        var end = html.IndexOf('"', start);
        return html.Substring(start, end - start);
    }

    private static MultipartFormDataContent BuildSingleFileForm(string filename, byte[] bytes)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent
        {
            { fileContent, "f0", filename }, { new StringContent("0"), "f0__lastModified" }
        };
        return form;
    }
}
