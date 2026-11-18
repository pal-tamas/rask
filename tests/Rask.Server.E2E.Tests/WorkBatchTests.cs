using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Background;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Cqrs;
using Rask.Data;
using Rask.Querying;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A batch its tenant shares, on real Rask.Jobs (#1368), at a desktop's width and a phone's: one press enqueues three
///     imports <c>.ByTenant()</c>; the one who pressed sees them as one group in the bell and gets one toast when the last
///     ends, a colleague in the same tenant sees the group and gets no toast, and a reader in another tenant sees nothing.
/// </summary>
public sealed class WorkBatchTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_batch_of_three_is_one_group_with_one_toast_its_tenant_sees_and_another_tenant_does_not(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-e2e-batch-{Guid.NewGuid():N}.db");
        var gate = new ImportGate();
        await using var host = await LiveServerHost.StartAsync<WorkBatchApp>(
            blockWebSockets: false, cookieSignIn: true,
            services: services =>
            {
                // As an app wires its data by hand: the session's scope is ambient, so its user and tenant are in flight.
                services.AddSingleton(gate).AddRaskData<WorkBatchDb>(o => o.UseSqlite($"Data Source={path}"));
                services.AddRaskJobs<WorkBatchDb>(o => o.PollInterval = TimeSpan.FromMilliseconds(100));
            });
        await using (var db = await host.Services.GetRequiredService<IDbContextFactory<WorkBatchDb>>().CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var viewport = new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = height } };
        try
        {
            await using var alice = await Reader.SignIn(playwright, host, viewport, "alice");
            await using var bob = await Reader.SignIn(playwright, host, viewport, "bob");
            await using var carol = await Reader.SignIn(playwright, host, viewport, "carol");

            await alice.Page.ClickAsync("#press");
            await Expect(alice.Bell.Locator("[data-ui-work-count]")).ToHaveTextAsync("3");
            await Expect(bob.Bell.Locator("[data-ui-work-count]")).ToHaveTextAsync("3");
            gate.Release();

            await alice.Page.Locator("[data-ui-work-bell]").ClickAsync();
            await Expect(alice.Page.Locator("[data-ui-work-flyout] [data-ui-work-batch=warned]")).ToBeVisibleAsync();
            await Expect(alice.Page.Locator("[data-ui-work-flyout] [data-ui-work-batch]")).ToHaveCountAsync(1);
            await Expect(alice.Page.Locator("[data-ui-work-flyout] [data-ui-work-item]")).ToHaveCountAsync(3);
            await Expect(alice.Page.Locator("[data-ui-work-flyout] [data-ui-work-detail]")).ToHaveTextAsync("No new rows in b.csv");
            await Expect(alice.Page.Locator("[data-ui-work-flyout] [data-ui-work-actions] a")).ToHaveTextAsync("All loads");
            await Expect(alice.Page.Locator("[data-ui-toast]").GetByText("3 items are done, some with a warning.")).ToHaveCountAsync(1);

            await bob.Page.Locator("[data-ui-work-bell]").ClickAsync();
            await Expect(bob.Page.Locator("[data-ui-work-flyout] [data-ui-work-batch=warned]")).ToBeVisibleAsync();
            await Expect(bob.Page.Locator("[data-ui-toast]")).Not.ToContainTextAsync("items");

            await carol.Page.Locator("[data-ui-work-bell]").ClickAsync();
            await Expect(carol.Page.Locator("[data-ui-work-flyout] [data-ui-work-none]")).ToBeVisibleAsync();
            await Expect(carol.Bell.Locator("[data-ui-work-count]")).ToHaveCountAsync(0);

            Assert.Empty(alice.Errors);
            Assert.Empty(bob.Errors);
            Assert.Empty(carol.Errors);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    /// <summary>One reader in a browser context of their own, signed in through the page.</summary>
    private sealed class Reader(IBrowserContext context, IPage page, List<string> errors) : IAsyncDisposable
    {
        public IPage Page => page;

        public ILocator Bell => page.Locator("header");

        public IReadOnlyList<string> Errors => errors;

        public static async Task<Reader> SignIn(PlaywrightFixture playwright, LiveServerHost host, BrowserNewContextOptions options, string who)
        {
            var context = await playwright.Browser.NewContextAsync(options);
            var page = await context.NewPageAsync();
            var errors = new List<string>();
            page.PageError += (_, error) => errors.Add(error);
            await SlowMachine.ApplyAsync(page);
            await page.GotoAsync(host.BaseUrl + "/work");
            await page.ClickAsync($"#as-{who}");
            await Expect(page.Locator("#who")).ToHaveTextAsync(who);
            return new Reader(context, page, errors);
        }

        public async ValueTask DisposeAsync() => await context.DisposeAsync();
    }
}

/// <summary>The app's own table of jobs.</summary>
public sealed class WorkBatchDb(DbContextOptions<WorkBatchDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskJobs();
}

/// <summary>Holds every import until the test lets them go, so each reader sees the batch running first.</summary>
public sealed class ImportGate
{
    private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Opened => _open.Task;

    public void Release() => _open.TrySetResult();
}

/// <summary>One file of a partner's load: waits for the gate, then is done — b.csv with a warning.</summary>
public sealed record ImportFile(string Name) : IJob;

public sealed class ImportFileHandler(ImportGate gate) : ICommandHandler<ImportFile>
{
    public async Task Handle(ImportFile command)
    {
        await gate.Opened.WaitAsync(Current.Cancellation);
        if (command.Name == "b.csv")
        {
            Jobs.Warn("No new rows in b.csv");
        }
    }
}

public sealed partial class WorkBatchApp : Component
{
    protected override Component? Render() => Router;
}

/// <summary>An app's header with the bell, three readers to sign in as, and the press that starts a partner's load.</summary>
[Route("/work")]
public sealed partial class WorkBatchPage(AuthSignIn auth, IUserProvider users) : Component
{
    private static readonly Guid Acme = Guid.Parse("9a3f6c55-0d4e-4a7b-b1c2-3c3c3c3c3c3c");
    private static readonly Guid Globex = Guid.Parse("c7e1d9a2-6b3f-4e8d-a5f4-4d4d4d4d4d4d");

    private static readonly Dictionary<string, (Guid User, Guid Tenant)> People = new(StringComparer.Ordinal)
    {
        ["alice"] = (Guid.Parse("0e4b6c1e-7b52-4f3c-9d2f-1a1a1a1a1a1a"), Acme),
        ["bob"] = (Guid.Parse("5d0f2a77-3c11-4b8e-8e0b-2b2b2b2b2b2b"), Acme),
        ["carol"] = (Guid.Parse("3f1e2d77-8a11-4c8e-9e0b-5e5e5e5e5e5e"), Globex),
    };

    protected override Component? HeadAssets =>
        [Markup.Title["partner loads"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override Component? Render() =>
        Div.Attributes(("data-rask-ui-document", ""))[
            Ui.Header[Ui.Spacer, Ui.WorkNotices.Actions(A.Href("/work")["All loads"])],
            Ui.Main[
                P.Id("who")[users.Current.Identity?.Name ?? "nobody"],
                People.Keys.Select(who => Button.Key(who).Id($"as-{who}").OnClick(() => SignIn(who))[$"Sign in as {who}"]),
                Button.Id("press").OnClick(Press)["Load the partner's files"]
            ],
            Ui.Toast
        ];

    private Task SignIn(string who)
    {
        var (user, tenant) = People[who];
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, who), new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim(Tenant.ClaimType, tenant.ToString())],
            LiveServerHost.CookieScheme);
        return auth.SignIn(new ClaimsPrincipal(identity), "/work", LiveServerHost.CookieScheme);
    }

    private async Task Press()
    {
        await Jobs.Enqueue([new ImportFile("a.csv"), new ImportFile("b.csv"), new ImportFile("c.csv")])
            .Noticed(file => $"Import of {file.Name}")
            .ByTenant();
    }
}
