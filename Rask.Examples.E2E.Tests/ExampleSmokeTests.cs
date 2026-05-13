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
            .ToContainTextAsync("required",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });
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
    public Task Primitives_RawFactory_RendersVerbatimHtml() => RunAsync(async () =>
    {
        // Proves the Raw(string) factory (added alongside the RASK014 ban on
        // `new`) reaches the browser as actual markup: a Text component would
        // HTML-encode the angle brackets and no <strong> element would appear
        // in the DOM.
        await Page.GotoAsync("/primitives");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Primitives",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var rawResult = Page.Locator(".sample-result-body p")
            .Filter(new LocatorFilterOptions { HasText = "Already" }).First;
        await Expect(rawResult.Locator("strong"))
            .ToHaveTextAsync("safe",
                new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
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

        // OnMountAsync awaits 450ms and triggers a re-render, OnPropsChangedAsync
        // fires for every render — so the page settles at some N >= 2 before we click.
        // Wait for the async log line that proves the awaited continuation ran, then
        // verify the click bumps the counter strictly higher.
        var badge = Page.Locator(".badge:has-text('Render #')").First;
        await Expect(Page.Locator("li code:has-text('OnMountAsync (after')"))
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
    public Task Boom_HandlerThrow_RendersBoundaryFallback_AndRecoverRestores() => RunAsync(async () =>
    {
        await Page.GotoAsync("/boom");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Error boundary",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Initial state: the healthy "Throw a handler exception" button is visible inside
        // its boundary's children — no fallback shown yet.
        await Expect(Page.Locator("#boom-throw")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#boom-fallback")).ToHaveCountAsync(0);

        // Click the throwing button → ErrorBoundary catches the handler exception and
        // replaces the subtree with its fallback. The healthy button must be gone.
        await Page.Locator("#boom-throw").ClickAsync();
        await Expect(Page.Locator("#boom-fallback").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#boom-fallback").First)
            .ToContainTextAsync("kaboom — handler boundary demo");
        await Expect(Page.Locator("#boom-throw")).ToHaveCountAsync(0);

        // The implicit root boundary should NOT be tripped — the navbar (which lives
        // outside the user boundary in ShowcaseLayout) is still visible.
        await Expect(Page.Locator(".navbar .navbar-brand"))
            .ToContainTextAsync("Rask");

        // Click "Recover" → boundary._error cleared, next render walks Children again
        // and the original button reappears.
        await Page.Locator("#boom-recover").First.ClickAsync();
        await Expect(Page.Locator("#boom-throw")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#boom-fallback")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task Boom_RenderThrow_RendersBoundaryFallback() => RunAsync(async () =>
    {
        await Page.GotoAsync("/boom");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Error boundary",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator("#boom-render-trigger")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Flipping the flag inserts a RenderThrower into the second boundary's children.
        // Its Render() throws synchronously, the HtmlSerializer rewinds the partial output,
        // the boundary trips, and the fallback replaces the subtree. Only that boundary
        // trips — so #boom-fallback should appear exactly once with the render-time message.
        await Page.Locator("#boom-render-trigger").ClickAsync();
        await Expect(Page.Locator("#boom-fallback").First)
            .ToContainTextAsync("kaboom — render-time boundary demo",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#boom-fallback")).ToHaveCountAsync(1);

        // Clicking Recover must ALSO reset _throwOnRender — otherwise the boundary clears
        // its error, re-walks Children, hits the same RenderThrower, and trips again. The
        // demo's fallback combines boundary.Recover() with resetting the flag, so the
        // healthy trigger button should reappear.
        await Page.Locator("#boom-recover").First.ClickAsync();
        await Expect(Page.Locator("#boom-render-trigger")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#boom-fallback")).ToHaveCountAsync(0);
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

    [Fact]
    public Task Props_RendersDataAttributeBareForm() => RunAsync(async () =>
    {
        // The "Data — dictionary expands as data-*" demo asserts a null value renders
        // as a bare attribute (data-new). If a future refactor breaks the null-value
        // path, this test would catch the regression in the rendered DOM.
        await Page.GotoAsync("/props");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Universal props",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var dataDiv = Page.Locator(".sample-result-body div[data-role='card']").First;
        await Expect(dataDiv).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(dataDiv).ToHaveAttributeAsync("data-index", "7");
        // Bare attribute: present, value empty.
        await Expect(dataDiv).ToHaveAttributeAsync("data-new", "");
    });

    [Fact]
    public Task Components_GeneratedFactory_RendersGreeting() => RunAsync(async () =>
    {
        // Exercises the generated factory pipeline end-to-end: the Greeting user
        // component is invoked via its source-generated factory inside ComponentsPage's
        // CodeSample, and the rendered <p> should contain the props that flowed through.
        await Page.GotoAsync("/components");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("User components",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var greeting = Page.Locator(".sample-result-body p").Filter(
            new LocatorFilterOptions { HasText = "Hello," }).First;
        await Expect(greeting).ToContainTextAsync("Dr.",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(greeting.Locator("strong")).ToHaveTextAsync("Ada");
    });

    [Fact]
    public Task Tags_RendersBlockquote() => RunAsync(async () =>
    {
        await Page.GotoAsync("/tags");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Tag factories",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator(".sample-result-body blockquote").First)
            .ToContainTextAsync("A small DSL",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Routing_NavigatorButton_NavigatesToUserDetail() => RunAsync(async () =>
    {
        // The /routing page hosts buttons that call Navigator.Navigate("/users/137").
        // Validates that an in-handler Navigator call resolves through the same code
        // path as a sidebar click — including parent-route + outlet rendering.
        await Page.GotoAsync("/routing");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Routing",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.GetByRole(AriaRole.Button,
                new PageGetByRoleOptions { Name = "/users/137" })
            .ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/users/137$"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("User #137",
                new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task ViewTransitions_NavigateButton_LandsOnTarget() => RunAsync(async () =>
    {
        // Smoke for the view-transitions demo: Navigator.Navigate in a handler must
        // still reach its destination on browsers without startViewTransition (the
        // runtime should fall back to a direct morph) — Chromium-with-it is the
        // happy path, but the assertion only cares about the final URL + DOM.
        await Page.GotoAsync("/view-transitions");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("View transitions",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.GetByRole(AriaRole.Button,
                new PageGetByRoleOptions { Name = "Go to /binding" })
            .ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/binding$"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Two-way binding",
                new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Cancellation_UnmountWhileRunning_LogsCancelled() => RunAsync(async () =>
    {
        // Validates Component.CancellationToken end-to-end through the showcase:
        // mount a probe that awaits Task.Delay(2500ms, CancellationToken) and
        // unmount it before the delay elapses. The probe's OperationCanceledException
        // catch appends "cancelled" to the page log, proving the framework cancelled
        // the lifetime token and the await unwound cleanly.
        await Page.GotoAsync("/cancellation");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Cancellation",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#cancel-mount").ClickAsync();
        // The probe's "running" pill should appear once the OnMountAsync continuation
        // has started — the StateHasChanged() before the await guarantees it.
        await Expect(Page.Locator(".cancel-probe-pill"))
            .ToContainTextAsync("running",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Unmount before the 2.5s Task.Delay completes — the framework cancels the
        // probe's CancellationToken, Task.Delay throws OperationCanceledException,
        // and the catch logs the cancellation.
        await Page.Locator("#cancel-unmount").ClickAsync();
        await Expect(Page.Locator(".cancel-log"))
            .ToContainTextAsync("cancelled",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Cancellation_LeaveRunning_LogsCompleted() => RunAsync(async () =>
    {
        // The complementary happy path: mount the probe and let the Task.Delay run
        // to completion. Without this test, a regression that silently cancels every
        // probe (e.g. a stray Cancel on every render) would still pass the test above.
        await Page.GotoAsync("/cancellation");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Cancellation",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#cancel-mount").ClickAsync();
        await Expect(Page.Locator(".cancel-log"))
            .ToContainTextAsync("completed",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Disposal_UnmountSyncProbe_FiresDispose() => RunAsync(async () =>
    {
        // Validates the IDisposable branch of ComponentLifecycle.DisposeComponentTree:
        // when the parent stops rendering the probe in its children, the framework
        // walks the diff and calls Dispose(). The probe's Dispose body logs into the
        // page list, so the test asserts the log entry appeared.
        await Page.GotoAsync("/disposal");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Disposal",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#dispose-sync-mount").ClickAsync();
        await Expect(Page.Locator(".dispose-probe-pill"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await Page.Locator("#dispose-sync-unmount").ClickAsync();
        await Expect(Page.Locator("#dispose-sync-log"))
            .ToContainTextAsync("disposed",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Disposal_UnmountAsyncProbe_FiresDisposeAsync() => RunAsync(async () =>
    {
        // The IAsyncDisposable variant: DisposeAsync includes a Task.Yield, so the
        // "async-disposed" log entry shows up after the next render cycle resolves
        // the continuation. The 10s timeout absorbs the round trip.
        await Page.GotoAsync("/disposal");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Disposal",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#dispose-async-mount").ClickAsync();
        await Expect(Page.Locator(".dispose-async-pill"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await Page.Locator("#dispose-async-unmount").ClickAsync();
        await Expect(Page.Locator("#dispose-async-log"))
            .ToContainTextAsync("async-disposed",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Components_SkipFactoryCounter_PreservesStateAcrossClicks() => RunAsync(async () =>
    {
        // Two things at once: [SkipFactory] left Initial settable via object initialiser,
        // so OnMount seeded _count to 7; and the framework caches the component by tree
        // position, so private _count survives clicks.
        await Page.GotoAsync("/components");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("User components",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var counter = Page.Locator("#skipfactory-counter");
        await Expect(counter)
            .ToContainTextAsync("Clicks: 7",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await counter.ClickAsync();
        await counter.ClickAsync();
        await counter.ClickAsync();
        await Expect(counter)
            .ToContainTextAsync("Clicks: 10",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Boom_NestedBoundary_InnerCatchesFirst() => RunAsync(async () =>
    {
        // Validates ErrorBoundary nesting precedence (Rask.Core/Components/ErrorBoundary.cs):
        // an exception thrown inside the inner boundary's subtree is caught by the
        // INNER boundary, not the outer one. The outer healthy region (a sibling of
        // the inner boundary, also inside the outer boundary) must remain mounted.
        await Page.GotoAsync("/boom");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Error boundary",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator("#boom-nested-throw"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await Page.Locator("#boom-nested-throw").ClickAsync();

        // Inner boundary fallback is shown.
        await Expect(Page.Locator("#boom-nested-inner-fallback"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Outer boundary did NOT trip — its fallback never rendered.
        await Expect(Page.Locator("#boom-nested-outer-fallback"))
            .ToHaveCountAsync(0);
        // The outer healthy region (sibling of the inner boundary inside the outer)
        // is still visible.
        await Expect(Page.Locator("#boom-nested-outer-healthy"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Recover restores the inner boundary; the throw button reappears.
        await Page.Locator("#boom-nested-inner-recover").ClickAsync();
        await Expect(Page.Locator("#boom-nested-throw"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#boom-nested-inner-fallback"))
            .ToHaveCountAsync(0);
    });

    [Fact]
    public Task Http_NavigateAwayBeforeLoad_NoLifecycleFault() => RunAsync(async () =>
    {
        // Validates Component.CancellationToken: visiting /http kicks off
        // HttpPage.OnMountAsync's HttpClient.GetFromJsonAsync(..., CancellationToken).
        // Navigating away before the fetch settles must cancel the in-flight
        // request and avoid logging "lifecycle hook on HttpPage faulted" — the
        // signal the framework prints when an unhandled exception bubbles out of
        // an async hook.
        await Page.GotoAsync("/http");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("HttpClient + DI",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Don't wait for the article — navigate away as soon as the page header
        // appears, while OnMountAsync's HTTP request is still in flight.
        await ClickSidebar("Welcome");
        await Expect(Page.Locator("h1.display-5"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // Give any erroring continuation a moment to flush to stderr.
        await Page.WaitForTimeoutAsync(500);

        Assert.DoesNotContain("lifecycle hook on HttpPage faulted", ServerLog);
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
