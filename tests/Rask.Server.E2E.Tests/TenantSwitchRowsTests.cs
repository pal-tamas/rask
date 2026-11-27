using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Cqrs;
using Rask.Querying;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a test app; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     An administrator of an app with its own sign-in chooses tenant A, then B, then A again from a live handler
///     (<c>Auth.ActInTenant</c>, redeemed through <c>MapRaskTenantChoice</c>), moving between the pages by the app's own
///     links. The list, a <c>Ui.Table</c> drawn from a query whose key does not name the tenant, shows only the rows
///     of the tenant chosen last — never, not even for one paint, a row it read for the tenant before.
/// </summary>
/// <remarks>
///     Every row text the table ever shows is recorded from the page (a <c>MutationObserver</c>), so a stale row that
///     a refetch replaced a moment later still fails the journey. Each read takes a moment, so a cached list would be
///     on screen while it runs.
/// </remarks>
public sealed class TenantSwitchRowsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };

    [Fact]
    public async Task A_tenant_chosen_by_a_live_handler_shows_only_its_own_rows_through_A_B_and_A_again()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var seen = new List<string[]>();

        await ChooseAsync(page, "a", "/tenant-home");
        await page.ClickAsync("#to-rows");
        seen.Add(await RowsAsync(page, "a", ["a-1", "a-2"]));
        await ChooseAsync(page, "b", "/tenant-rows");
        seen.Add(await RowsAsync(page, "b", ["b-1", "b-2", "b-3"]));
        await page.ClickAsync("#to-home");
        await page.ClickAsync("#to-rows");
        seen.Add(await RowsAsync(page, "b", ["b-1", "b-2", "b-3"]));
        await page.ClickAsync("#to-home");
        await ChooseAsync(page, "a", "/tenant-home");
        await page.ClickAsync("#to-rows");
        seen.Add(await RowsAsync(page, "a", ["a-1", "a-2"]));

        Assert.Equal<string[]>([["a-1", "a-2"], ["b-1", "b-2", "b-3"], ["b-1", "b-2", "b-3"], ["a-1", "a-2"]], seen);
        Assert.Empty(session.PageErrors);
    }

    private async Task<HookSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<TenantSwitchApp>(
            playwright, path: "/sign-in", cookieSignIn: true, beside: SignsIn,
            beforeLoad: page => page.AddInitScriptAsync(Recorder),
            services: services =>
            {
                services.AddRaskQuery();
                services.AddSingleton<IDispatcher, NothingToDispatch>();
            });
        await Expect(session.Page.Locator("#tenant")).ToHaveTextAsync("tenant=none", Settled);
        return session;
    }

    // Presses the tenant's button and waits for the page the choice loads again, connected and in that tenant.
    private static async Task ChooseAsync(IPage page, string tenant, string path)
    {
        await ConnectedAsync(page);
        await page.ClickAsync("#choose-" + tenant);
        await Expect(page.Locator("#tenant")).ToHaveTextAsync("tenant=" + tenant, Settled);
        await Expect(page).ToHaveURLAsync(new Uri(new Uri(page.Url), path).ToString());
        await ConnectedAsync(page);
    }

    // The rows once settled, after checking every row the table showed since this document loaded or the last check
    // belonged to the tenant the page is in.
    private static async Task<string[]> RowsAsync(IPage page, string tenant, string[] expected)
    {
        await Expect(page.Locator("#tenant")).ToHaveTextAsync("tenant=" + tenant, Settled);
        var read = await page.WaitForFunctionAsync(SettledRows, expected, new() { Timeout = 15_000 });
        var rows = await read.JsonValueAsync<string[][]>();
        var (shown, seen) = (rows[0], rows[1]);

        var foreign = seen.Where(row => !row.StartsWith(tenant + "-", StringComparison.Ordinal)).ToArray();
        Assert.True(foreign.Length == 0, $"in tenant {tenant} the table showed rows of another: {string.Join(", ", foreign)}");
        return shown;
    }

    // What the table shows and what the recorder saw, read and cleared in ONE call in ONE document, once the table
    // shows the expected rows AND the recorder has seen them. Checking the table with one call and reading the
    // recorder with another raced: when the document changed in between (the tab loading its page again, as one
    // whose connection is reopened for a cookie the tenant choice wrote may), the read met a recorder that had seen
    // nothing yet (run 45853675787). A MutationObserver also reports a moment after the change it saw.
    private const string SettledRows =
        """
        expected => {
            if (!window.__rows) return false;
            const shown = [...document.querySelectorAll('#rows tbody tr td:first-child')].map(td => td.textContent.trim());
            if (shown.join('\n') !== expected.join('\n') || !expected.every(row => window.__rows.has(row))) return false;
            const seen = [...window.__rows];
            window.__rows.clear();
            return [shown, seen];
        }
        """;

    private static Task ConnectedAsync(IPage page) =>
        page.WaitForFunctionAsync("() => document.body.inert === false && !document.querySelector('.rask-overlay[data-show]')");

    // Every first-cell text the rows table shows, at every change of the page.
    private const string Recorder =
        """
        (() => {
            window.__rows = new Set();
            const look = () => {
                for (const td of document.querySelectorAll('#rows tbody tr td:first-child')) {
                    const text = td.textContent.trim();
                    if (text) window.__rows.add(text);
                }
            };
            const start = () => {
                look();
                new MutationObserver(look).observe(document.documentElement, { subtree: true, childList: true, characterData: true });
            };
            if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
        })();
        """;

    // The app's own sign-in: an administrator with no tenant, and its tenant choice writing the chosen one into the cookie.
    private static void SignsIn(WebApplication app)
    {
        app.MapGet("/sign-in", async context =>
        {
            await context.SignInAsync(LiveServerHost.CookieScheme, TenantSwitchTenants.Admin(null));
            context.Response.Redirect("/tenant-home");
        });
        app.MapRaskTenantChoice(
            (Guid id, HttpContext _) => Task.FromResult(TenantSwitchTenants.Find(id)),
            (context, tenant) => context.SignInAsync(LiveServerHost.CookieScheme, TenantSwitchTenants.Admin(tenant)));
    }

    // A query cache needs a dispatcher. This one's query reads through its own lambda.
    private sealed class NothingToDispatch : IDispatcher
    {
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send(ICommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(
            ISubscription<TEvent> subscription, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();
    }
}

/// <summary>A tenant the app knows: its id and the short name its rows begin with.</summary>
public sealed record TenantSwitchTenant(Guid Id, string Name, string[] Rows);

/// <summary>The two tenants, and the principal of the administrator acting in one of them, or in none.</summary>
public static class TenantSwitchTenants
{
    public static readonly TenantSwitchTenant A = new(Guid.Parse("a0000000-0000-0000-0000-00000000000a"), "a", ["a-1", "a-2"]);

    public static readonly TenantSwitchTenant B = new(Guid.Parse("b0000000-0000-0000-0000-00000000000b"), "b", ["b-1", "b-2", "b-3"]);

    public static TenantSwitchTenant? Find(Guid id) => id == A.Id ? A : id == B.Id ? B : null;

    public static TenantSwitchTenant? Named(string? name) => name == A.Name ? A : name == B.Name ? B : null;

    public static ClaimsPrincipal Admin(TenantSwitchTenant? tenant)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, "ada"), new(ClaimTypes.Name, "ada"), new(ClaimTypes.Role, "admin"),
        ];
        if (tenant is not null)
        {
            claims.Add(new Claim("tenant", tenant.Name));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, LiveServerHost.CookieScheme));
    }

    public static string? Of(ClaimsPrincipal user) => user.FindFirst("tenant")?.Value;
}

public sealed partial class TenantSwitchApp : Component
{
    protected override Component? HeadAssets => [Markup.Title["tenant switch"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>The tenant the page is in, a button per tenant whose handler chooses it, and the way to the other page.</summary>
public sealed partial class TenantSwitchChooser : Component
{
    public required string Tenant { get; set; }

    public required string Back { get; set; }

    protected override Component? Render() => Div[
        P.Id("tenant")["tenant=" + Tenant],
        Ui.Button.Id("choose-a").OnClick(async () => await Rask.Core.Auth.ActInTenant(TenantSwitchTenants.A.Id, Back))["Act in A"],
        Ui.Button.Id("choose-b").OnClick(async () => await Rask.Core.Auth.ActInTenant(TenantSwitchTenants.B.Id, Back))["Act in B"]
    ];
}

[Route("/tenant-home")]
[AllowAnonymous]
public sealed partial class TenantSwitchHomePage(IUserProvider users) : Component
{
    protected override Component? Render() => Main[
        TenantSwitchChooser.Tenant(TenantSwitchTenants.Of(users.Current) ?? "none").Back("/tenant-home"),
        A.Id("to-rows").Href(Routes.TenantSwitchRowsPage())["Rows"]
    ];
}

/// <summary>The tenant's rows, read through a query whose key is the same in every tenant.</summary>
[Route("/tenant-rows")]
[AllowAnonymous]
public sealed partial class TenantSwitchRowsPage : Component
{
    private readonly IUserProvider _users;
    private readonly Query<string[]> _rows;

    public TenantSwitchRowsPage(IQueryClient queries, IUserProvider users)
    {
        _users = users;
        _rows = queries.Query(QueryKey.Of("tenant-rows"), async _ =>
        {
            var tenant = TenantSwitchTenants.Named(TenantSwitchTenants.Of(users.Current));
            await Task.Delay(150);
            return tenant?.Rows ?? [];
        });
    }

    protected override Component? Render() => Main[
        TenantSwitchChooser.Tenant(TenantSwitchTenants.Of(_users.Current) ?? "none").Back("/tenant-rows"),
        A.Id("to-home").Href(Routes.TenantSwitchHomePage())["Home"],
        Ui.Table.Id("rows").Of(_rows)[
            Ui.TableColumns[Ui.TableColumn["Row"], Ui.TableColumn["Tenant"]],
            Ui.TableRows[(_rows.Data ?? []).Select(row => Ui.TableRow.Key(row)[
                Ui.TableCell[row], Ui.TableCell[row[..1].ToUpper(CultureInfo.InvariantCulture)]
            ])]
        ]
    ];
}
