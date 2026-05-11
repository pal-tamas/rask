using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Rask.Examples.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract class ExampleSmokeTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;

    protected IPage Page = default!;

    protected ExampleSmokeTests(PlaywrightFixture pw) => _pw = pw;

    protected abstract string BaseUrl { get; }
    protected abstract string FixtureName { get; }
    protected abstract string ServerLog { get; }

    public async Task InitializeAsync()
    {
        _ctx = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl });
        Page = await _ctx.NewPageAsync();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public Task Home_LoadsAndShowsHelloWorld() => RunAsync(async () =>
    {
        await Page.GotoAsync("/");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Hello, world!");
    });

    [Fact]
    public Task Counter_ClicksThreeTimes_ReachesThree() => RunAsync(async () =>
    {
        await Page.GotoAsync("/counter");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Counter");
        await Expect(Page.Locator("p.fs-5")).ToHaveTextAsync("Current count: 0");

        var btn = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Click me" });
        for (var i = 0; i < 3; i++) await btn.ClickAsync();

        await Expect(Page.Locator("p.fs-5"))
            .ToHaveTextAsync("Current count: 3", new LocatorAssertionsToHaveTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Weather_AnonymousUser_RedirectsToLogin() => RunAsync(async () =>
    {
        await Page.GotoAsync("/weather");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/login\\?.*[Rr]eturn[Uu]rl=.*"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Sign in");
    });

    [Fact]
    public Task Weather_AnonymousLogin_LandsBackOnWeather() => RunAsync(async () =>
    {
        await Page.GotoAsync("/weather");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/login\\?.*[Rr]eturn[Uu]rl=.*"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Page.Locator("input[name=username]").FillAsync("alice");
        await Page.Locator("input[name=password]").FillAsync("password");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/weather$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });
        await Expect(Page.Locator("h1"))
            .ToHaveTextAsync("Weather", new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Login_WithInvalidCredentials_ShowsError() => RunAsync(async () =>
    {
        await Page.GotoAsync("/login");
        await Page.Locator("input[name=username]").FillAsync("alice");
        await Page.Locator("input[name=password]").FillAsync("wrong");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();

        await Expect(Page.Locator(".alert-danger"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page).ToHaveURLAsync(new Regex(".*/login(\\?.*)?$"));
    });

    [Fact]
    public Task Weather_LoadsFiveRows() => RunAsync(async () =>
    {
        await SignInAsync();
        await Page.GotoAsync("/weather");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Weather");
        await Expect(Page.Locator("table.table tbody tr"))
            .ToHaveCountAsync(5, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Logout_ClearsSession() => RunAsync(async () =>
    {
        await SignInAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/$"), new PageAssertionsToHaveURLOptions { Timeout = 10_000 });

        await Page.GotoAsync("/weather");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/login(\\?.*)?$"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Navigation_BetweenPages_PreservesSpaContext() => RunAsync(async () =>
    {
        await SignInAsync();
        await Page.GotoAsync("/");
        await Page.EvaluateAsync("() => { window.__raskSentinel = 'alive'; }");

        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Counter" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/counter$"));
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Counter");
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));

        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Weather" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/weather$"));
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Weather");
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));
    });

    [Fact]
    public Task BackForwardNavigation_PreservesSpaState() => RunAsync(async () =>
    {
        await Page.GotoAsync("/");
        await Page.EvaluateAsync("() => { window.__raskSentinel = 'alive'; }");

        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Counter" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/counter$"));

        await Page.GoBackAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/$"));
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Hello, world!");
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));

        await Page.GoForwardAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/counter$"));
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Counter");
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));
    });

    [Fact]
    public Task UnknownRoute_LoadsWithoutNetworkError() => RunAsync(async () =>
    {
        var response = await Page.GotoAsync("/this-route-definitely-does-not-exist");
        Assert.NotNull(response);
        // Either 200 (SPA fallback) or 404 — both are non-5xx, the page should still mount.
        Assert.True((int)response!.Status < 500, $"unexpected status {response.Status}");
        // The nav bar / app shell is still present, proving the app loaded.
        await Expect(Page.Locator("a.navbar-brand")).ToHaveTextAsync("Rask",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task RapidClick_TenClicksInSuccession_FinalCountIsTen() => RunAsync(async () =>
    {
        await Page.GotoAsync("/counter");
        await Expect(Page.Locator("p.fs-5")).ToHaveTextAsync("Current count: 0");

        var btn = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Click me" });
        for (var i = 0; i < 10; i++) await btn.ClickAsync();

        await Expect(Page.Locator("p.fs-5"))
            .ToHaveTextAsync("Current count: 10", new LocatorAssertionsToHaveTextOptions { Timeout = 8_000 });
    });

    private async Task SignInAsync(string username = "alice", string password = "password")
    {
        await Page.GotoAsync("/login");
        await Page.Locator("input[name=username]").FillAsync(username);
        await Page.Locator("input[name=password]").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await Expect(Page.Locator("text=Signed in as"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }

    private async Task RunAsync(Func<Task> body, [CallerMemberName] string testName = "")
    {
        try
        {
            await body();
        }
        finally
        {
            await TestArtifacts.DumpAsync(Page, FixtureName, testName, ServerLog);
        }
    }
}
