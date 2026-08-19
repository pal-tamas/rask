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
///     How a multipart body's parts are paired back to the properties they came from. A part carries no
///     property name — only the index the message's JSON wrote where the file's contents would go — so
///     this pairing is the whole of it, and getting it wrong hands a handler the wrong file rather than
///     failing.
/// </summary>
public sealed class UploadPairingTests
{
    [Fact]
    public async Task Eleven_files_reach_the_properties_they_were_sent_for()
    {
        // The regression this exists for: pairing by sorting the part names as text puts "10" between
        // "1" and "2", so from ten files up every file after the tenth lands on somebody else's
        // property. Nothing throws — the handler simply reads the wrong file.
        var response = await SendManyAsync(Enumerable.Range(0, 11).ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "\"file-0,file-1,file-2,file-3,file-4,file-5,file-6,file-7,file-8,file-9,file-10\"",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_order_parts_arrive_in_does_not_decide_where_they_land()
    {
        // A proxy, a client or a retry may reorder parts. The index each part names is the only thing
        // that may decide its property.
        var reversed = Enumerable.Range(0, 11).Reverse().ToArray();

        var response = await SendManyAsync(reversed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "\"file-0,file-1,file-2,file-3,file-4,file-5,file-6,file-7,file-8,file-9,file-10\"",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_part_named_something_other_than_an_index_is_refused()
    {
        var response = await SendPartsAsync([("message", null), ("file", "a.txt")]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Two_parts_claiming_one_index_are_refused()
    {
        // Ambiguous rather than merely odd: one of the two would win silently, and which one is decided
        // by arrival order.
        var response = await SendPartsAsync([("message", null), ("0", "a.txt"), ("0", "b.txt")]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_part_is_refused_rather_than_handed_over_as_a_gap()
    {
        // Index 1 never arrives. Letting it through would give the handler a null where the message's
        // own shape says a file must be.
        var response = await SendPartsAsync([("message", null), ("0", "a.txt"), ("2", "c.txt")]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_upload_over_the_cap_is_refused_before_the_body_is_read()
    {
        // The cap must bound what the server accepts, not merely what it reports afterwards: reading the
        // form first spools the whole upload to disk, so a limit applied on the far side of it has
        // already let the sender write. Kestrel enforces the limit set here while reading.
        using var client = Host(o => o.MaxUploadBytes = 1024).CreateClient();
        using var request = Request(HttpMethod.Post, "Rask.Cqrs.Server.Tests.Uploaded");

        var content = new MultipartFormDataContent
        {
            { new StringContent("""{"note":"hi","file":0}""", Encoding.UTF8, "application/json"), "message" },
        };
        var part = new ByteArrayContent(new byte[64 * 1024]);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(part, "0", "big.bin");
        request.Content = content;

        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendManyAsync(int[] order)
    {
        var parts = new List<(string Name, string? File)> { ("message", null) };
        foreach (var index in order)
        {
            parts.Add((index.ToString(System.Globalization.CultureInfo.InvariantCulture), $"f{index}.txt"));
        }

        return await SendPartsAsync(parts, manyFiles: true);
    }

    private static async Task<HttpResponseMessage> SendPartsAsync(
        IReadOnlyList<(string Name, string? File)> parts,
        bool manyFiles = false)
    {
        var name = manyFiles ? "Rask.Cqrs.Server.Tests.UploadMany" : "Rask.Cqrs.Server.Tests.Uploaded";

        // MaxFileCount defaults to 8, which is below the count that exposes the pairing bug — so the
        // default would hide it behind a 413 rather than let the mispairing happen.
        using var client = Host(manyFiles ? static o => o.MaxFileCount = 16 : null).CreateClient();
        using var request = Request(HttpMethod.Post, name);

        var content = new MultipartFormDataContent();
        foreach (var (partName, fileName) in parts)
        {
            if (fileName is null)
            {
                content.Add(new StringContent(Message(manyFiles), Encoding.UTF8, "application/json"), partName);
                continue;
            }

            // The body is what the index claims it is: each file's contents name the index it was sent
            // for, so a mispairing shows up as content rather than as a count.
            var body = manyFiles ? $"file-{partName}" : "payload";
            var part = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            part.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            content.Add(part, partName, fileName);
        }

        request.Content = content;
        return await client.SendAsync(request);
    }

    private static string Message(bool manyFiles)
    {
        if (!manyFiles)
        {
            return """{"note":"hi","file":0}""";
        }

        var builder = new StringBuilder("{");
        for (var i = 0; i <= 10; i++)
        {
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"\"f{i}\":{i}");
            if (i < 10)
            {
                builder.Append(',');
            }
        }

        return builder.Append('}').ToString();
    }

    private static HttpRequestMessage Request(HttpMethod method, string name)
    {
        var request = new HttpRequestMessage(method, $"/_rask/cqrs/request/{Uri.EscapeDataString(name)}");
        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.RequestHeader, RemoteEndpointDefaults.RequestHeaderValue);
        request.Headers.TryAddWithoutValidation("X-Test-User", "tester");
        return request;
    }

    private static TestServer Host(Action<RaskCqrsServerOptions>? configure = null)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddLogging(b => b.ClearProviders());
                services.AddRouting();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, PairingAuthHandler>("Test", static _ => { });
                services.AddAuthorization();
                services.AddRaskCqrsServer(configure);
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

    private sealed class PairingAuthHandler(
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
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, "Test")));
        }
    }
}
