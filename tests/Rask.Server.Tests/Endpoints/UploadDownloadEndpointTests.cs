using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Files;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

public class UploadDownloadEndpointTests
{
    [Fact]
    public async Task Upload_UnknownSession_Returns404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var form = BuildSingleFileForm("hi.txt", new byte[] { 1, 2, 3 });

        var response = await host.Http.PostAsync("/_rask/upload/no-such-session", form);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ValidMultipart_StagesFile_AndReturnsTokens()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);

        var form = BuildSingleFileForm("data.bin", new byte[] { 9, 8, 7, 6, 5 });
        var response = await host.Http.PostAsync("/_rask/upload/" + sessionId, form);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var files = doc.RootElement.GetProperty("files");
        Assert.Equal(1, files.GetArrayLength());
        var f = files[0];
        Assert.Equal("data.bin", f.GetProperty("name").GetString());
        Assert.Equal(5, f.GetProperty("size").GetInt64());

        var store = host.Server.Services.GetRequiredService<SessionUploadStore>();
        var entry = store.Get(sessionId, f.GetProperty("token").GetString()!);
        Assert.NotNull(entry);
        var staged = await File.ReadAllBytesAsync(entry!.Path);
        Assert.Equal(new byte[] { 9, 8, 7, 6, 5 }, staged);
    }

    [Fact]
    public async Task Download_KnownToken_ReturnsBytesWithDisposition_OneShot()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);

        var store = host.Server.Services.GetRequiredService<SessionDownloadStore>();
        var bytes = Encoding.UTF8.GetBytes("rask-report");
        var entry = store.StageBytes(sessionId, "report.txt", bytes, "text/plain");

        var url = $"/_rask/download/{sessionId}/{entry.Token}";
        var first = await host.Http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var content = await first.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, content);
        Assert.Equal("text/plain", first.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", first.Content.Headers.ContentDisposition?.ToString() ?? "");
        // The staged content-type is attacker-influenceable (echoed from the upload), so the
        // download must forbid MIME sniffing alongside forcing an attachment download.
        Assert.True(first.Headers.TryGetValues("X-Content-Type-Options", out var nosniff));
        Assert.Equal("nosniff", nosniff.Single());

        // One-shot: a second fetch returns 404.
        var second = await host.Http.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Download_UnknownToken_Returns404()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var response = await host.Http.GetAsync("/_rask/download/missing-session/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_FilenameWithPath_ReturnsSanitizedLeafName()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);

        var form = BuildSingleFileForm("../../etc/passwd", new byte[] { 1, 2, 3 });
        var response = await host.Http.PostAsync("/_rask/upload/" + sessionId, form);

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var name = doc.RootElement.GetProperty("files")[0].GetProperty("name").GetString();
        // The directory components are stripped before the name is stored and echoed, so a host
        // that surfaces it cannot be steered into a traversal (and it must still HTML-encode it).
        Assert.Equal("passwd", name);
    }

    [Fact]
    public async Task Upload_CrossOrigin_IsRejected()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);

        var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/upload/" + sessionId)
        {
            Content = BuildSingleFileForm("data.bin", new byte[] { 1, 2, 3 })
        };
        request.Headers.Add("Origin", "http://evil.example");

        var response = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_CrossOrigin_IsRejected()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await CreateSessionAsync(host);

        var store = host.Server.Services.GetRequiredService<SessionDownloadStore>();
        var entry = store.StageBytes(sessionId, "report.txt", Encoding.UTF8.GetBytes("secret"), "text/plain");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/_rask/download/{sessionId}/{entry.Token}");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // The cross-origin attempt must not consume the one-shot entry — a legitimate same-origin
        // fetch still succeeds afterward.
        var legit = await host.Http.GetAsync($"/_rask/download/{sessionId}/{entry.Token}");
        Assert.Equal(HttpStatusCode.OK, legit.StatusCode);
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
