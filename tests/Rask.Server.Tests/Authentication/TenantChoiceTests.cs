using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;
using Rask.Wire;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     An app with its own sign-in lets an administrator choose a tenant through <c>MapRaskTenantChoice</c> (#1280):
///     Rask answers the request and keeps the rules, the app's <c>onChosen</c> writes the choice into its sign-in.
/// </summary>
public sealed class TenantChoiceTests
{
    private static readonly Guid Acme = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    public async Task An_allowed_caller_s_choice_reaches_on_chosen()
    {
        var chosen = new Chosen();
        using var host = Host(chosen);

        var response = await ChooseAsync(host, Acme.ToString(), HeaderUser.Admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Partner?[] { new Partner(Acme) }, chosen.Tenants);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_numbered_app_s_find_is_handed_the_number()
    {
        var chosen = new Chosen();
        using var host = RaskTestHost.Create<SignInTestApp>(
            Authentication,
            app => Endpoints(app).MapRaskTenantChoice(
                (long number, HttpContext _) => Task.FromResult<Partner?>(new Partner(Guid.Empty, number)),
                chosen.Write));

        var response = await ChooseAsync(host, "42", HeaderUser.Admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Partner?[] { new Partner(Guid.Empty, 42) }, chosen.Tenants);
    }

    [Fact]
    public async Task A_choice_another_site_sends_is_refused_without_reaching_on_chosen()
    {
        var chosen = new Chosen();
        using var host = Host(chosen);

        var response = await ChooseAsync(host, Acme.ToString(), HeaderUser.Admin, fetchSite: "cross-site");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(chosen.Tenants);
    }

    [Fact]
    public async Task A_user_who_is_not_an_administrator_is_refused_without_reaching_on_chosen()
    {
        var chosen = new Chosen();
        using var host = Host(chosen);

        var response = await ChooseAsync(host, Acme.ToString(), HeaderUser.Member);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(chosen.Tenants);
    }

    [Fact]
    public async Task An_administrator_with_a_tenant_of_their_own_is_refused_without_reaching_on_chosen()
    {
        var chosen = new Chosen();
        using var host = Host(chosen, o => o.BelongsToTenant = user => user.HasClaim(c => c.Type == HeaderUser.OwnTenant));

        var response = await ChooseAsync(host, Acme.ToString(), HeaderUser.AdminOfOwnTenant);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(chosen.Tenants);
    }

    [Fact]
    public async Task Nobody_signed_in_is_refused_without_reaching_on_chosen()
    {
        var chosen = new Chosen();
        using var host = Host(chosen);

        var response = await ChooseAsync(host, Acme.ToString(), user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(chosen.Tenants);
    }

    [Fact]
    public async Task An_id_the_app_does_not_know_is_refused_without_reaching_on_chosen()
    {
        var chosen = new Chosen();
        using var host = Host(chosen);

        var unknown = await ChooseAsync(host, Guid.NewGuid().ToString(), HeaderUser.Admin);
        var notATenant = await ChooseAsync(host, "next door", HeaderUser.Admin);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notATenant.StatusCode);
        Assert.Empty(chosen.Tenants);
    }

    [Fact]
    public async Task A_request_without_the_required_header_is_refused_without_reaching_on_chosen()
    {
        var chosen = new Chosen();
        using var host = Host(chosen);

        var response = await ChooseAsync(host, Acme.ToString(), HeaderUser.Admin, requestHeader: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(chosen.Tenants);
    }

    [Fact]
    public async Task Leaving_hands_on_chosen_no_tenant()
    {
        var chosen = new Chosen();
        using var host = Host(chosen);

        var response = await ChooseAsync(host, tenant: null, HeaderUser.Admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Partner?[] { null }, chosen.Tenants);
    }

    [Fact]
    public async Task A_live_handler_s_choice_reaches_on_chosen_on_the_browser_s_redeem()
    {
        var chosen = new Chosen();
        using var host = Host(chosen);
        var ticket = host.Services.GetRequiredService<IAuthTicketStore>()
            .Issue(AuthAction.SignIn, null, null, "session-1", tenantPick: new TenantPick(Acme.ToString()));

        using var redeem = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket, session = "session-1" }),
        };
        redeem.Headers.Add(HeaderUser.Header, HeaderUser.Admin);
        var response = await host.Http.SendAsync(redeem, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Partner?[] { new Partner(Acme) }, chosen.Tenants);
    }

    [Fact]
    public async Task A_live_handler_s_choice_is_held_for_the_relay_once_a_choice_is_mapped()
    {
        using var mapped = Host(new Chosen());
        using var unmapped = RaskTestHost.Create<SignInTestApp>(Authentication);
        using var mappedScope = mapped.Services.CreateScope();
        using var unmappedScope = unmapped.Services.CreateScope();
        var signIn = mappedScope.ServiceProvider.GetRequiredService<AuthSignIn>();
        var withoutChoice = unmappedScope.ServiceProvider.GetRequiredService<AuthSignIn>();

        using (signIn.EnterHandler())
        {
            await signIn.ChooseTenant("42", "/orders");
        }

        Assert.True(signIn.TryConsume(out var pending));
        Assert.Equal("42", pending.TenantPick?.Tenant);
        Assert.Equal("/orders", pending.ReturnUrl);
        using (withoutChoice.EnterHandler())
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => withoutChoice.ChooseTenant("42", "/orders"));
        }
    }

    private static RaskTestHost Host(Chosen chosen, Action<TenantChoiceOptions>? configure = null) =>
        RaskTestHost.Create<SignInTestApp>(
            Authentication,
            app => Endpoints(app).MapRaskTenantChoice(
                (Guid id, HttpContext _) => Task.FromResult<Partner?>(id == Acme ? new Partner(Acme) : null),
                chosen.Write,
                configure));

    private static void Authentication(IServiceCollection services)
    {
        services.AddAuthentication(HeaderUser.Scheme)
            .AddScheme<AuthenticationSchemeOptions, HeaderUser>(HeaderUser.Scheme, configureOptions: null);
        services.AddAuthorization();
    }

    private static IEndpointRouteBuilder Endpoints(IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return (IEndpointRouteBuilder)app;
    }

    private static async Task<HttpResponseMessage> ChooseAsync(
        RaskTestHost host, string? tenant, string? user, string fetchSite = "same-origin", bool requestHeader = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenant")
        {
            Content = JsonContent.Create(new ActInTenantRequest(tenant)),
        };
        if (requestHeader)
        {
            request.Headers.Add(AuthApi.RequestHeader, "1");
        }

        request.Headers.Add("Sec-Fetch-Site", fetchSite);
        if (user is not null)
        {
            request.Headers.Add(HeaderUser.Header, user);
        }

        return await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>What the app knows a tenant as.</summary>
    private sealed record Partner(Guid Id, long Number = 0);

    /// <summary>What the app's <c>onChosen</c> was handed, as a cookie or a token would be written from it.</summary>
    private sealed class Chosen
    {
        private readonly ConcurrentQueue<Partner?> _tenants = new();

        public IReadOnlyList<Partner?> Tenants => [.. _tenants];

        public Task Write(HttpContext context, Partner? tenant)
        {
            _tenants.Enqueue(tenant);
            return Task.CompletedTask;
        }
    }

    /// <summary>The app's own sign-in, standing in for a refresh-token cookie or a JWT: a header names the user.</summary>
    private sealed class HeaderUser(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "AppSignIn";
        public const string Header = "X-Test-User";
        public const string OwnTenant = "own-tenant";
        public const string Admin = "admin";
        public const string Member = "member";
        public const string AdminOfOwnTenant = "admin-of-own-tenant";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[Header].ToString() is not { Length: > 0 } user)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new(ClaimTypes.Name, user)];
            if (user != Member)
            {
                claims.Add(new Claim(ClaimTypes.Role, "admin"));
            }

            if (user == AdminOfOwnTenant)
            {
                claims.Add(new Claim(OwnTenant, "7"));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
