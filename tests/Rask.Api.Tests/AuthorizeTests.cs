using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rask.Api.Tests;

/// <summary>
///     <c>[Authorize]</c> on a controller, enforced.
/// </summary>
/// <remarks>
///     <para>
///         This suite exists because <c>AddRaskApi</c> registers <c>AddMvcCore()</c> rather than
///         <c>AddControllers()</c>, and the question that raises is whether the leaner registration still
///         enforces <c>[Authorize]</c>. It does — the authorization middleware reads the attribute as
///         endpoint metadata, which MVC surfaces under the core registration too.
///     </para>
///     <para>
///         Worth a suite anyway, because of how it would fail if that were ever untrue. The attribute
///         would still compile, still read as protection to anyone looking at the controller, and the
///         endpoint would answer <b>200 to an anonymous caller</b> — a silent authorization bypass, the
///         worst failure this package could have. A registration change is exactly the kind of edit that
///         could cause it, so the enforcement is asserted over real HTTP rather than reasoned about.
///     </para>
///     <para>
///         What the app still owes: <c>AddAuthentication</c>/<c>AddAuthorization</c> and the two
///         <c>Use…</c> calls. Those are the app's, not this package's — and their absence fails loudly at
///         startup rather than quietly at request time.
///     </para>
/// </remarks>
public sealed class AuthorizeTests
{
    private const string Scheme = "Test";

    private static async Task<IHost> StartAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication(Scheme)
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(Scheme, _ => { });
                    services.AddAuthorization();
                    services.AddRaskApi();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapRaskApi());
                }))
            .StartAsync();
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync("/api/secret");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_authenticated_caller_is_let_through()
    {
        // The other half: a guard that refuses everyone is not protection, it is an outage.
        using var host = await StartAsync();

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "ada");

        var response = await client.GetAsync("/api/secret");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // `ada`, not `"ada"`. This client sends no Accept header, so content negotiation hands a
        // string-returning action to StringOutputFormatter and the answer is text/plain. The generated
        // API client asks for application/json precisely so it never meets this — see ApiCall.SendAsync.
        Assert.Equal("ada", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_role_the_caller_lacks_is_refused()
    {
        using var host = await StartAsync();

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "ada");

        var response = await client.GetAsync("/api/secret/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_role_the_caller_holds_is_allowed()
    {
        using var host = await StartAsync();

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "root");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "admin");

        var response = await client.GetAsync("/api/secret/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllowAnonymous_still_opens_a_protected_controller()
    {
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync("/api/secret/open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Signs in whoever the request names, so authorization has something to act on.</summary>
    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user) || user.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new(ClaimTypes.Name, user[0]!)];

            if (Request.Headers.TryGetValue("X-Test-Roles", out var roles))
            {
                claims.AddRange(roles.ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(role => new Claim(ClaimTypes.Role, role)));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

/// <summary>A controller that is protected, so the protection can be observed.</summary>
[ApiController]
[Route("api/secret")]
[Authorize]
public sealed class SecretController : ControllerBase
{
    /// <summary>Answers with the caller's name.</summary>
    [HttpGet("")]
    public ActionResult<string> Get() => User.Identity?.Name ?? "anonymous";

    /// <summary>Answers only for an admin.</summary>
    [HttpGet("admin")]
    [Authorize(Roles = "admin")]
    public ActionResult<string> Admin() => "admin";

    /// <summary>Answers for anybody, on an otherwise protected controller.</summary>
    [HttpGet("open")]
    [AllowAnonymous]
    public ActionResult<string> Open() => "open";
}
