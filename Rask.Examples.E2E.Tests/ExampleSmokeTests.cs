using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
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
    public Task Home_RendersHero() => RunAsync(async () =>
    {
        await Page.GotoAsync("/");
        await Expect(Page.Locator("h1.display-5"))
            .ToContainTextAsync("The Rask framework",
                new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
    });

    [Fact]
    public Task Sidebar_NavigatesToTagsPage() => RunAsync(async () =>
    {
        await Page.GotoAsync("/");
        await Expect(Page.Locator("h1.display-5"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Tag factories");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/tags$"));
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Tag factories");
    });

    [Fact]
    public Task Events_ClickCounter_IncrementsThreeTimes() => RunAsync(async () =>
    {
        await Page.GotoAsync("/events");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Events",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var clickButton = Page.Locator(".sample-result-body button:has-text('Clicks:')").First;
        for (var i = 0; i < 3; i++)
        {
            await clickButton.ClickAsync();
        }

        await Expect(clickButton)
            .ToContainTextAsync("Clicks: 3", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Events_InputChange_UpdatesEcho() => RunAsync(async () =>
    {
        await Page.GotoAsync("/events");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Events",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var input = Page.Locator(".sample-result-body input[type=text]:not([name])").First;
        await input.FillAsync("Hello Rask");

        await Expect(Page.Locator(".sample-result-body").Filter(new LocatorFilterOptions { HasText = "You typed:" }))
            .ToContainTextAsync("Hello Rask", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Events_FormSubmit_DisplaysSubmittedName() => RunAsync(async () =>
    {
        await Page.GotoAsync("/events");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Events",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("input[name=name]").FillAsync("Ada");
        await Page.Locator("button[type=submit]").ClickAsync();

        await Expect(Page.Locator(".sample-result-body")
                .Filter(new LocatorFilterOptions { HasText = "Last submitted:" }))
            .ToContainTextAsync("Ada", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Binding_TypedBindUpdatesEcho() => RunAsync(async () =>
    {
        await Page.GotoAsync("/binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // The Bind helper derives the input's name attribute from the expression's
        // property — `() => _model.Name` produces <input name="Name">, which the
        // manual Value+OnInput section does not emit, so the locator is unique.
        var bound = Page.Locator("input[name=Name]").First;
        await bound.FillAsync("Ada");

        await Expect(Page.Locator(".sample-result-body").Filter(new LocatorFilterOptions { HasText = "Hello," }))
            .ToContainTextAsync("Ada", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_InvalidSubmit_ShowsRequiredMessages() => RunAsync(async () =>
    {
        await Page.GotoAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // The per-field demo's form is the one containing #v1-name.
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();

        await Expect(Page.Locator("form:has(#v1-name) .text-danger").First)
            .ToContainTextAsync("required", new LocatorAssertionsToContainTextOptions
            {
                Timeout = 10_000,
                IgnoreCase = true
            });
    });

    [Fact]
    public Task Validation_ValidSubmit_RendersSuccessBanner() => RunAsync(async () =>
    {
        await Page.GotoAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#v1-name").FillAsync("Ada Lovelace");
        await Page.Locator("#v1-email").FillAsync("ada@example.com");
        await Page.Locator("#v1-age").FillAsync("28");
        await Page.Locator("#v1-plan").SelectOptionAsync("pro");
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();

        await Expect(Page.Locator(".alert-success").First)
            .ToContainTextAsync("Ada Lovelace", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Validation_FixingInvalidField_HidesItsMessage() => RunAsync(async () =>
    {
        await Page.GotoAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // 1. Submit empty → every field reports an error.
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();
        var nameField = Page.Locator("form:has(#v1-name) div:has(> #v1-name)");
        await Expect(nameField.Locator(".text-danger"))
            .ToContainTextAsync("required",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });

        // 2. Fill a valid value into Name.
        await Page.Locator("#v1-name").FillAsync("Ada Lovelace");

        // 3. The Name field's error message must disappear once it becomes valid,
        //    even though focus hasn't left the input (only the input event has fired).
        await Expect(nameField.Locator(".text-danger"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // 4. Other fields still report their own errors — only Name cleared.
        await Expect(Page.Locator("form:has(#v1-name) div:has(> #v1-email) .text-danger"))
            .ToContainTextAsync("required",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000, IgnoreCase = true });
    });

    [Fact]
    public Task Http_LoadsPostFromJsonPlaceholder() => RunAsync(async () =>
    {
        await Page.GotoAsync("/http");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("HttpClient + DI",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var article = Page.Locator(".sample-result-body article.card");
        await Expect(article).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Expect(article.Locator("h3")).Not.ToBeEmptyAsync();
        await Expect(article.Locator("p")).Not.ToBeEmptyAsync();
    });

    [Fact]
    public Task ScopedCss_TwoComponents_RenderDifferentColors() => RunAsync(async () =>
    {
        await Page.GotoAsync("/scoped-css");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Scoped CSS",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var boxes = Page.Locator(".sample-result-body .box");
        await Expect(boxes).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        var firstBg = await boxes.Nth(0).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        var secondBg = await boxes.Nth(1).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.NotEqual(firstBg, secondBg);
    });

    [Fact]
    public Task Routing_UserDetail_BindsRouteParamAndQuery() => RunAsync(async () =>
    {
        await Page.GotoAsync("/users/42?tab=profile");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("User #42",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator("li:has-text('Id')").Locator("strong"))
            .ToHaveTextAsync("42");
        await Expect(Page.Locator("li:has-text('Tab')").Locator("strong"))
            .ToHaveTextAsync("profile");
    });

    [Fact]
    public Task Navigator_SetQuery_ReflectsInUrl() => RunAsync(async () =>
    {
        await Page.GotoAsync("/navigator");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Navigator",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.GetByRole(AriaRole.Button,
                new PageGetByRoleOptions { Name = "SetQuery sort=asc" })
            .ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*\\?sort=asc.*"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Expect(Page.Locator("main").Filter(new LocatorFilterOptions { HasText = "Query" }))
            .ToContainTextAsync("sort=asc",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Lifecycle_TriggerReRender_IncrementsCounter() => RunAsync(async () =>
    {
        await Page.GotoAsync("/lifecycle");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Lifecycle hooks",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // OnInitializedAsync awaits 450ms and triggers a re-render, OnParametersSetAsync
        // fires for every render — so the page settles at some N >= 2 before we click.
        // Wait for the async log line that proves the awaited continuation ran, then
        // verify the click bumps the counter strictly higher.
        var badge = Page.Locator(".badge:has-text('Render #')").First;
        await Expect(Page.Locator("li code:has-text('OnInitializedAsync (after')"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Server fixture keeps emitting extra renders for a moment; let the dust settle.
        await Page.WaitForTimeoutAsync(500);

        var before = ExtractRenderCount(await badge.TextContentAsync());
        await Page.Locator("button:has-text('Trigger re-render')").ClickAsync();

        await Expect(badge).Not.ToContainTextAsync($"Render #{before}",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        var after = ExtractRenderCount(await badge.TextContentAsync());
        Assert.True(after > before, $"expected render counter to increase from {before}, was {after}");
    });

    private static int ExtractRenderCount(string? text) =>
        int.Parse(Regex.Match(text ?? "0", @"\d+").Value);

    [Fact]
    public Task UnknownRoute_RendersNotFoundPage() => RunAsync(async () =>
    {
        var response = await Page.GotoAsync("/this-route-definitely-does-not-exist");
        Assert.NotNull(response);
        Assert.True(response!.Status < 500, $"unexpected status {response.Status}");
        // The navbar (which sits outside Outlet) is still rendered.
        await Expect(Page.Locator(".navbar .navbar-brand"))
            .ToContainTextAsync("Rask", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        // The [NotFound]-decorated page renders inside ShowcaseLayout's Outlet.
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Page not found",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("main p.lead"))
            .ToContainTextAsync("/this-route-definitely-does-not-exist",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Navigation_PreservesSpaContext() => RunAsync(async () =>
    {
        await Page.GotoAsync("/");
        await Expect(Page.Locator("h1.display-5"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Page.EvaluateAsync("() => { window.__raskSentinel = 'alive'; }");

        await ClickSidebar("Tag factories");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/tags$"));
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));

        await ClickSidebar("Events");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/events$"));
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));
    });

    [Fact]
    public Task BackForwardNavigation_PreservesSpaState() => RunAsync(async () =>
    {
        await Page.GotoAsync("/");
        await Expect(Page.Locator("h1.display-5"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Page.EvaluateAsync("() => { window.__raskSentinel = 'alive'; }");

        await ClickSidebar("Tag factories");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/tags$"));

        await Page.GoBackAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/$"));
        await Expect(Page.Locator("h1.display-5")).ToBeVisibleAsync();
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));

        await Page.GoForwardAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/tags$"));
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Tag factories");
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));
    });

    protected Task ClickSidebar(string label) =>
        Page.Locator("aside.side-nav button.nav-item-btn:has-text(\"" + label + "\")").ClickAsync();

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
