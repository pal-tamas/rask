using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rask.Cqrs.Server.Tests;

// The endpoint is an RPC surface on a public origin, so every rule it enforces gets a test that would
// fail if the rule were removed. Run against a real host: routing, authentication and model binding all
// participate in the behaviour under test, and a hand-called handler would exercise none of them.
public sealed class EndpointSecurityTests
{
    [Fact]
    public async Task A_request_without_the_header_is_refused_before_anything_else()
    {
        // The CSRF control. Cross-site markup cannot set a custom header, so this is what makes adding a
        // GET surface safe.
        using var client = Host().CreateClient();
        var response = await client.GetAsync(Url("Rask.Cqrs.Server.Tests.GetPublicStats", "{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_message_name_is_404_once_the_caller_is_known()
    {
        var response = await Send(HttpMethod.Get, "Nope.NotAMessage", "{}", authenticated: true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_message_with_no_handler_here_is_indistinguishable_from_an_unknown_one()
    {
        // From outside, "I don't know that name" and "I know it but cannot serve it" must look the same:
        // the difference is a map of the server's internals. It must also not become a 500 - the
        // dispatcher would otherwise throw its no-handler exception into one, paging someone over a
        // request that was never serviceable.
        var response = await Send(HttpMethod.Get, "Rask.Cqrs.Server.Tests.Unhandled", "{}", authenticated: true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_tell_a_real_message_name_from_a_typo()
    {
        // Enumeration guard: if a known name answered 401 while an unknown one answered 404, an
        // unauthenticated attacker could map every message the app has, one guess at a time.
        var known = await Send(HttpMethod.Get, "Rask.Cqrs.Server.Tests.GetSecret", "{\"id\":1}");
        var bogus = await Send(HttpMethod.Get, "Rask.Cqrs.Server.Tests.NoSuchThing", "{}");

        Assert.Equal(HttpStatusCode.Unauthorized, known.StatusCode);
        Assert.Equal(known.StatusCode, bogus.StatusCode);
    }

    [Fact]
    public async Task A_command_cannot_be_triggered_by_a_GET()
    {
        // Verb integrity: a mutating message must not be reachable by a URL, a prefetch or a link scanner.
        DeleteThingHandler.Deleted = 0;

        var response = await Send(HttpMethod.Get, "Rask.Cqrs.Server.Tests.DeleteThing", """{"id":9}""", authenticated: true);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(0, DeleteThingHandler.Deleted);
    }

    [Fact]
    public async Task An_anonymous_request_is_401_by_default()
    {
        // Authenticated-required is the default, so a message whose author never thought about auth is
        // rejected rather than exposed.
        var response = await Send(HttpMethod.Get, "Rask.Cqrs.Server.Tests.GetSecret", """{"id":1}""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_handler_marked_AllowAnonymous_is_the_way_past_that_default()
    {
        var response = await Send(HttpMethod.Get, "Rask.Cqrs.Server.Tests.GetPublicStats", "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("7", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Roles_on_the_handler_are_enforced_rather_than_quietly_ignored()
    {
        // The failure mode worth guarding: an author writes [Authorize(Roles = "admin")] and believes it
        // is enforced. If the manifest ever stops carrying Roles, this returns 204 instead of 403.
        var forbidden = await Send(HttpMethod.Post, "Rask.Cqrs.Server.Tests.AdminPurge", "{}", authenticated: true);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var allowed = await Send(
            HttpMethod.Post, "Rask.Cqrs.Server.Tests.AdminPurge", "{}", authenticated: true, role: "admin");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task A_policy_on_the_handler_is_enforced()
    {
        var forbidden = await Send(HttpMethod.Post, "Rask.Cqrs.Server.Tests.MembersOnly", "{}", authenticated: true);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var allowed = await Send(
            HttpMethod.Post, "Rask.Cqrs.Server.Tests.MembersOnly", "{}", authenticated: true, claim: "member");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task A_query_round_trips_over_GET()
    {
        var response = await Send(
            HttpMethod.Get, "Rask.Cqrs.Server.Tests.GetSecret", """{"id":42}""", authenticated: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"secret-42\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_result_is_never_stored_by_a_shared_cache()
    {
        var response = await Send(
            HttpMethod.Get, "Rask.Cqrs.Server.Tests.GetSecret", """{"id":1}""", authenticated: true);

        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_runs_and_answers_204()
    {
        DeleteThingHandler.Deleted = 0;

        var response = await Send(
            HttpMethod.Post, "Rask.Cqrs.Server.Tests.DeleteThing", """{"id":11}""", authenticated: true);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(11, DeleteThingHandler.Deleted);
    }

    [Fact]
    public async Task A_handler_failure_never_leaks_what_went_wrong()
    {
        var response = await Send(HttpMethod.Get, "Rask.Cqrs.Server.Tests.Explodes", "{}", authenticated: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_oversized_body_is_refused()
    {
        using var client = Host(o => o.MaxRequestBytes = 64).CreateClient();
        using var request = Request(HttpMethod.Post, "Rask.Cqrs.Server.Tests.DeleteThing", authenticated: true);
        request.Content = new StringContent($$"""{"id":1,"pad":"{{new string('x', 500)}}"}""", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_download_filename_is_reduced_to_a_safe_leaf()
    {
        // The handler returns "../../etc/passwd". A traversal segment reaching a Content-Disposition is a
        // real attack, not a hypothetical one.
        var response = await Send(HttpMethod.Get, "Rask.Cqrs.Server.Tests.Export", "{}", authenticated: true);
        var disposition = response.Content.Headers.ContentDisposition!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("..", disposition.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/", disposition.ToString(), StringComparison.Ordinal);
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task An_uploaded_file_reaches_the_handler_as_a_readable_stream()
    {
        using var client = Host().CreateClient();
        using var request = Request(HttpMethod.Post, "Rask.Cqrs.Server.Tests.Uploaded", authenticated: true);

        var content = new MultipartFormDataContent
        {
            { new StringContent("""{"note":"hi","file":0}""", Encoding.UTF8, "application/json"), "message" },
        };
        var part = new ByteArrayContent("payload"u8.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(part, "0", "a.txt");
        request.Content = content;

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"hi:payload\"", await response.Content.ReadAsStringAsync());
    }

    private static string Url(string name, string message) =>
        $"/_rask/cqrs/request/{Uri.EscapeDataString(name)}"
        + $"?{RemoteEndpointDefaults.MessageQueryParameter}={Uri.EscapeDataString(message)}";

    private static HttpRequestMessage Request(
        HttpMethod method,
        string name,
        bool authenticated = false,
        string? role = null,
        string? claim = null)
    {
        var request = new HttpRequestMessage(method, $"/_rask/cqrs/request/{Uri.EscapeDataString(name)}");
        request.Headers.Add(RemoteEndpointDefaults.RequestHeader, RemoteEndpointDefaults.RequestHeaderValue);

        if (authenticated)
        {
            request.Headers.Add("X-Test-User", "ada");
        }

        if (role is not null)
        {
            request.Headers.Add("X-Test-Role", role);
        }

        if (claim is not null)
        {
            request.Headers.Add("X-Test-Claim", claim);
        }

        return request;
    }

    private static async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string name,
        string message,
        bool authenticated = false,
        string? role = null,
        string? claim = null)
    {
        using var client = Host().CreateClient();
        using var request = Request(method, name, authenticated, role, claim);

        if (method == HttpMethod.Get)
        {
            request.RequestUri = new Uri(Url(name, message), UriKind.Relative);
        }
        else
        {
            request.Content = new StringContent(message, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
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
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", static _ => { });
                services.AddAuthorization(o => o.AddPolicy(
                    "members", p => p.RequireClaim("membership", "member")));
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

    // Authenticates from request headers so a test states the identity it means inline, rather than
    // through a sign-in flow that has nothing to do with what is under test.
    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };

            if (Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            if (Request.Headers.TryGetValue("X-Test-Claim", out var membership))
            {
                claims.Add(new Claim("membership", membership.ToString()));
            }

            var identity = new ClaimsIdentity(claims, "Test");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
