using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rask.Cqrs.Server;

namespace Rask.Cqrs.Server.Tests;

/// <summary>
///     A file too large for one request arrives in pieces before the message that carries it, and the
///     handler cannot tell the difference.
/// </summary>
/// <remarks>
///     The reason this path exists: a browser's <c>fetch</c> reads a request body into memory before
///     sending it, so a single-shot upload costs its own size in the tab. Every host reads a
///     <c>RaskFile</c> in bounded slices already — chunking is what keeps the <em>request</em> bounded too.
/// </remarks>
public sealed class ChunkedUploadTests
{
    [Fact]
    public async Task A_file_sent_in_chunks_reaches_the_handler_whole()
    {
        using var server = Host();
        using var client = server.CreateClient();

        // Deliberately not a multiple of the chunk size, so the last chunk is a short one — the case an
        // off-by-one in the offset arithmetic would survive with round numbers.
        var payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abcdefghij", 250)) + "TAIL");
        const string uploadId = "0123456789abcdef";

        await SendChunksAsync(client, uploadId, payload, chunk: 1000);

        var response = await SendMessageAsync(client, uploadId, """{"note":"hi","file":0}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $"\"hi:{Encoding.UTF8.GetString(payload)}\"",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_chunk_at_the_wrong_offset_is_refused_and_says_where_the_server_is()
    {
        // The whole point of an offset: a retried chunk must not append twice. Answering 409 with the
        // offset the server holds is what lets the client continue instead of starting again.
        using var server = Host();
        using var client = server.CreateClient();

        await SendChunkAsync(client, "aaaa", 0, 0, new byte[100]);
        var response = await SendChunkAsync(client, "aaaa", 0, 0, new byte[100]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "100",
            response.Headers.GetValues(RemoteEndpointDefaults.UploadOffsetHeader).Single());
    }

    [Fact]
    public async Task An_upload_session_is_spent_once()
    {
        // An id that stayed valid could replay somebody's upload into a second message.
        using var server = Host();
        using var client = server.CreateClient();

        await SendChunksAsync(client, "bbbb", "hello"u8.ToArray(), chunk: 8);

        var first = await SendMessageAsync(client, "bbbb", """{"note":"a","file":0}""");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await SendMessageAsync(client, "bbbb", """{"note":"b","file":0}""");
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task One_users_upload_cannot_be_spent_by_another()
    {
        // An upload id is not a capability. Scoping the session to the caller who opened it is what stops
        // a guessed id being used to inject bytes into somebody else's message.
        using var server = Host();
        using var client = server.CreateClient();

        await SendChunksAsync(client, "cccc", "secret"u8.ToArray(), chunk: 8, user: "alice");

        var response = await SendMessageAsync(client, "cccc", """{"note":"x","file":0}""", user: "mallory");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_chunk_without_the_csrf_header_is_refused()
    {
        using var server = Host();
        using var client = server.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/_rask/cqrs/request/{RemoteEndpointDefaults.UploadSegment}")
        {
            Content = new ByteArrayContent(new byte[10]),
        };
        request.Headers.TryAddWithoutValidation("X-Test-User", "alice");
        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadHeader, "dddd");
        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadFileHeader, "0");
        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadOffsetHeader, "0");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_chunk_is_refused_when_the_app_requires_a_user()
    {
        using var server = Host();
        using var client = server.CreateClient();

        var response = await SendChunkAsync(client, "eeee", 0, 0, new byte[10], user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task SendChunksAsync(
        HttpClient client, string uploadId, byte[] payload, int chunk, string user = "alice")
    {
        long offset = 0;
        while (offset < payload.Length)
        {
            var take = (int)Math.Min(chunk, payload.Length - offset);
            var slice = payload.AsSpan((int)offset, take).ToArray();

            var response = await SendChunkAsync(client, uploadId, 0, offset, slice, user);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            offset += take;
        }
    }

    private static async Task<HttpResponseMessage> SendChunkAsync(
        HttpClient client, string uploadId, int index, long offset, byte[] body, string? user = "alice")
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/_rask/cqrs/request/{RemoteEndpointDefaults.UploadSegment}")
        {
            Content = new ByteArrayContent(body),
        };

        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.RequestHeader, RemoteEndpointDefaults.RequestHeaderValue);
        if (user is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Test-User", user);
        }

        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadHeader, uploadId);
        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.UploadFileHeader, index.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.UploadOffsetHeader, offset.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadNameHeader, "receipt%201.txt");
        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadTypeHeader, "text%2Fplain");

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendMessageAsync(
        HttpClient client, string uploadId, string json, string user = "alice")
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/_rask/cqrs/request/Rask.Cqrs.Server.Tests.Uploaded")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.RequestHeader, RemoteEndpointDefaults.RequestHeaderValue);
        request.Headers.TryAddWithoutValidation("X-Test-User", user);
        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadHeader, uploadId);

        return await client.SendAsync(request);
    }

    private static TestServer Host()
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddLogging(b => b.ClearProviders());
                services.AddRouting();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, ChunkAuthHandler>("Test", static _ => { });
                services.AddAuthorization();
                services.AddRaskCqrsServer();
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e => e.MapRaskCqrs());
            });
        });

        var host = builder.Start();
        return host.GetTestServer();
    }

    private sealed class ChunkAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user) || user.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new System.Security.Claims.ClaimsIdentity("Test");
            identity.AddClaim(new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.Name, user.ToString()));

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(identity), "Test")));
        }
    }
}
