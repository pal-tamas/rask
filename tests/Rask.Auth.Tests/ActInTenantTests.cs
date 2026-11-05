using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
///     An administrator acts in a tenant they choose (#1280): the choice is kept on the sign-in's session, so the
///     next request, a reconnect and a live page's next event all work in it, and signing out drops it.
/// </summary>
[Collection(AuthDbCollection.Name)]
public sealed class ActInTenantTests
{
    private const string Password = "Password1";
    private const string Owner = "owner@example.com";
    private const string None = "none";

    private static readonly Guid Acme = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    public async Task An_administrator_s_choice_is_the_tenant_of_the_next_request_and_of_a_reconnect()
    {
        using var app = TenantApp();
        using var browser = app.Client();
        var cookie = await RegisterOwnerAsync(browser);

        var chosen = await ChooseAsync(browser, Acme.ToString());

        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        Assert.Equal(Acme.ToString(), await TenantNowAsync(browser));
        using var reconnected = app.Client();
        Assert.Equal(Acme.ToString(), await TenantNowAsync(reconnected, cookie));
    }

    [Fact]
    public async Task A_user_who_is_not_an_administrator_is_refused()
    {
        using var app = TenantApp();
        using var owner = app.Client();
        await RegisterOwnerAsync(owner);
        using var member = app.Client();
        var registered = await PostAsync(member, AuthApi.Register, new { email = "member@example.com", password = Password });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        var chosen = await ChooseAsync(member, Acme.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, chosen.StatusCode);
        Assert.Equal(None, await TenantNowAsync(member));
    }

    [Fact]
    public async Task A_choice_another_site_asks_for_is_refused()
    {
        using var app = TenantApp();
        using var browser = app.Client();
        await RegisterOwnerAsync(browser);

        var chosen = await ChooseAsync(browser, Acme.ToString(), fetchSite: "cross-site");

        Assert.Equal(HttpStatusCode.Forbidden, chosen.StatusCode);
        Assert.Equal(None, await TenantNowAsync(browser));
    }

    [Fact]
    public async Task Signing_out_drops_the_choice()
    {
        using var app = TenantApp();
        using var browser = app.Client();
        await RegisterOwnerAsync(browser);
        Assert.Equal(HttpStatusCode.NoContent, (await ChooseAsync(browser, Acme.ToString())).StatusCode);

        await PostAsync(browser, AuthApi.Logout, new { });
        var signedIn = await PostAsync(browser, AuthApi.Login, new { email = Owner, password = Password });

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(None, await TenantNowAsync(browser));
    }

    [Fact]
    public async Task A_numbered_tenant_is_chosen_by_its_number_and_leaving_it_acts_in_none_again()
    {
        using var app = TenantApp();
        using var browser = app.Client();
        await RegisterOwnerAsync(browser);

        await ChooseAsync(browser, "42");
        var numbered = await TenantNowAsync(browser);
        await ChooseAsync(browser, null);

        Assert.Equal(new Guid("00000000-0000-0000-0000-00000000002a").ToString(), numbered);
        Assert.Equal(None, await TenantNowAsync(browser));
    }

    [Fact]
    public async Task A_live_page_holding_the_principal_from_before_the_choice_is_handed_one_in_the_chosen_tenant()
    {
        await using var harness = new AuthHarness();
        var (sessions, held) = await SignedInOwnerAsync(harness);

        var result = await Accounts(harness).ActInTenantAsync(held, Acme, TestContext.Current.CancellationToken);
        var revalidated = await sessions.RevalidateAsync(SessionOf(held), held, used: true, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotSame(held, revalidated);
        Assert.Equal(Acme.ToString(), revalidated!.FindFirstValue(Tenant.ClaimType));
    }

    [Fact]
    public async Task Taking_the_role_away_takes_the_chosen_tenant_with_it()
    {
        await using var harness = new AuthHarness();
        var (sessions, held) = await SignedInOwnerAsync(harness);
        await Accounts(harness).ActInTenantAsync(held, Acme, TestContext.Current.CancellationToken);

        await using (var db = harness.NewContext())
        {
            var owner = await db.Set<TestUser>().SingleAsync(u => u.Email == Owner, TestContext.Current.CancellationToken);
            owner.RevokeRole(RaskRoles.Admin);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var resumed = await sessions.ResumeAsync(SessionOf(held), TestContext.Current.CancellationToken);

        Assert.NotNull(resumed);
        Assert.Null(resumed.FindFirstValue(Tenant.ClaimType));
    }

    [Fact]
    public async Task Choosing_is_off_when_no_role_may_choose()
    {
        await using var harness = new AuthHarness(o => o.ActInTenantRole = null);
        var (_, held) = await SignedInOwnerAsync(harness);

        var result = await Accounts(harness).ActInTenantAsync(held, Acme, TestContext.Current.CancellationToken);

        Assert.Equal(AuthError.NotAllowed, result.Error);
    }

    // An app with one endpoint that says which tenant its request works in, read the way a tenant filter reads it.
    private static EndpointApp TenantApp() =>
        new(
            services => services
                .AddHttpContextAccessor()
                .AddScoped<IPrincipalSource>(sp => new RequestPrincipal(sp.GetRequiredService<IHttpContextAccessor>())),
            endpoints => endpoints.MapGet("/tenant-now", (HttpContext http) =>
            {
                using (Db.UseScope(http.RequestServices))
                {
                    return Tenant.Key?.ToString() ?? None;
                }
            }));

    private static async Task<string> RegisterOwnerAsync(HttpClient client)
    {
        var response = await PostAsync(client, AuthApi.Register, new
        {
            email = Owner,
            password = Password,
            firstRunToken = EndpointApp.Token,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The cookie alone, as a browser that reconnects presents it.
        return response.Headers.GetValues("Set-Cookie").First().Split(';')[0];
    }

    private static async Task<HttpResponseMessage> ChooseAsync(HttpClient client, string? tenant, string fetchSite = "same-origin")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthApi.DefaultPrefix + AuthApi.Tenant)
        {
            Content = JsonContent.Create(new { tenant }),
        };
        request.Headers.Add(RaskAuthDefaults.RequestHeader, "1");
        request.Headers.Add("Sec-Fetch-Site", fetchSite);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthApi.DefaultPrefix + route)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(RaskAuthDefaults.RequestHeader, "1");

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> TenantNowAsync(HttpClient client, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant-now");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static IAccounts Accounts(AuthHarness harness) => harness.Services.GetRequiredService<IAccounts>();

    private static Guid SessionOf(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sid")!);

    // The owner signed in on one device: the session row and the principal a page holds for it.
    private static async Task<(IAuthSessions Sessions, ClaimsPrincipal Held)> SignedInOwnerAsync(AuthHarness harness)
    {
        await harness.StartAsync();
        var registered = await Accounts(harness).RegisterAsync(Owner, Password, AuthHarness.FirstRunTokenValue, client: null, TestContext.Current.CancellationToken);
        Assert.True(registered.Result.Succeeded);
        var sessions = harness.Services.GetRequiredService<IAuthSessions>();
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await sessions.StartAsync(owner.Id, null, null, persistent: false, TestContext.Current.CancellationToken);

        return (sessions, (await sessions.ResumeAsync(sessionId, TestContext.Current.CancellationToken))!);
    }

    private sealed class RequestPrincipal(IHttpContextAccessor http) : IPrincipalSource
    {
        public ClaimsPrincipal? Current => http.HttpContext?.User;
    }
}
