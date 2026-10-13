using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     Flux's carousel on the running site, under a real pointer and real keys.
/// </summary>
/// <remarks>
///     What a unit test cannot see: that the track scrolls and snaps, and that the runtime's carousel hook keeps
///     the arrows, the slides' marks and the indicators in step with it — with nothing sent to the page.
/// </remarks>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitCarouselTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task The_arrows_move_a_slide_at_a_time_and_each_leaves_at_the_end_it_points_to() => RunAsync(async () =>
    {
        var carousel = await OpenAsync("basic");
        var previous = carousel.Locator("[data-direction='previous']");
        var next = carousel.Locator("[data-direction='next']");
        await Expect(Slide(carousel, 1)).ToHaveAttributeAsync("data-selected", "");
        await Expect(previous).ToHaveCSSAsync("opacity", "0");
        await Expect(previous.Locator("button")).ToBeDisabledAsync();

        await next.Locator("button").ClickAsync();

        await Expect(Slide(carousel, 2)).ToHaveAttributeAsync("data-selected", "");
        await Expect(Slide(carousel, 1)).Not.ToHaveAttributeAsync("data-selected", "");
        await Expect(previous).ToHaveCSSAsync("opacity", "1");
        await Expect(previous.Locator("button")).ToBeEnabledAsync();
        await next.Locator("button").ClickAsync();
        await Expect(Slide(carousel, 3)).ToHaveAttributeAsync("data-selected", "");
        await Expect(carousel).ToHaveAttributeAsync("data-at-end", "");
        await Expect(next).ToHaveCSSAsync("opacity", "0");
        await Expect(next.Locator("button")).ToBeDisabledAsync();
        await previous.Locator("button").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Slide(carousel, 2)).ToHaveAttributeAsync("data-selected", "");
        // The slide really is in view: the track stands where its second slide starts.
        var track = (await carousel.Locator("[data-ui-carousel-track]").BoundingBoxAsync())!;
        var shown = (await Slide(carousel, 2).BoundingBoxAsync())!;
        Assert.Equal(track.X, shown.X, 1.0);
    });

    [Fact]
    public Task An_indicator_goes_to_its_slide_and_follows_a_scroll_made_by_hand() => RunAsync(async () =>
    {
        var carousel = await OpenAsync("indicators");
        var dots = carousel.Locator("[data-rask-carousel-indicators] button");
        await Expect(dots).ToHaveCountAsync(3);
        await Expect(dots.Nth(0)).ToHaveAttributeAsync("aria-current", "true");

        await dots.Nth(2).ClickAsync();

        await Expect(Slide(carousel, 3)).ToHaveAttributeAsync("data-selected", "");
        await Expect(dots.Nth(2)).ToHaveAttributeAsync("aria-current", "true");
        await Expect(dots.Nth(0)).Not.ToHaveAttributeAsync("aria-current", "true");
        await carousel.Locator("[data-ui-carousel-track]").EvaluateAsync("track => track.scrollTo({ left: 0, behavior: 'instant' })");
        await Expect(dots.Nth(0)).ToHaveAttributeAsync("aria-current", "true");
        await Expect(Slide(carousel, 1)).ToHaveAttributeAsync("data-selected", "");
    });

    [Fact]
    public Task Controls_elsewhere_on_the_page_move_the_carousel_that_carries_their_name_and_dim_at_its_ends() => RunAsync(async () =>
    {
        var carousel = await OpenAsync("external");
        var controls = Page.Locator("[data-testid='ui-carousel-external'] [data-ui-carousel-controls]");
        var previous = controls.Locator("[data-direction='previous']");
        var next = controls.Locator("[data-direction='next']");
        var track = carousel.Locator("[data-ui-carousel-track]");
        await Expect(previous).ToHaveCSSAsync("opacity", "0.5");
        await Expect(carousel.Locator("[data-direction]")).ToHaveCountAsync(0);

        await next.Locator("button").ClickAsync();

        await Expect(previous).ToHaveCSSAsync("opacity", "1");
        await Expect(previous.Locator("button")).ToBeEnabledAsync();
        Assert.True(await track.EvaluateAsync<double>("track => track.scrollLeft") > 0, "the named carousel did not move.");
        await previous.Locator("button").ClickAsync();
        await Expect(carousel).ToHaveAttributeAsync("data-at-start", "");
        await Expect(previous).ToHaveCSSAsync("opacity", "0.5");
    });

    [Fact]
    public Task Autoplay_moves_on_by_itself_and_holds_under_the_pointer() => RunAsync(async () =>
    {
        var carousel = await OpenAsync("autoplay");
        await Page.Mouse.MoveAsync(2, 2);

        await Expect(Slide(carousel, 2)).ToHaveAttributeAsync("data-selected", "", new LocatorAssertionsToHaveAttributeOptions { Timeout = 8_000 });

        await carousel.Locator("[data-ui-carousel-track]").HoverAsync();
        await Page.WaitForTimeoutAsync(4_500);
        await Expect(Slide(carousel, 2)).ToHaveAttributeAsync("data-selected", "");
        await Page.Mouse.MoveAsync(2, 2);
        await Expect(Slide(carousel, 3)).ToHaveAttributeAsync("data-selected", "", new LocatorAssertionsToHaveAttributeOptions { Timeout = 8_000 });
    });

    [Fact]
    public Task Page_advance_moves_by_the_slides_in_view_rewind_goes_back_to_the_first_and_disabled_moves_nothing() => RunAsync(async () =>
    {
        var paged = await OpenAsync("page");
        var rewinding = Page.Locator("[data-testid='ui-carousel-rewind'] [data-ui-carousel]");
        var disabled = Page.Locator("[data-testid='ui-carousel-disabled'] [data-ui-carousel]");

        await paged.Locator("[data-direction='next'] button").ClickAsync();

        await Expect(Slide(paged, 1)).Not.ToHaveAttributeAsync("data-selected", "");
        await Expect(Slide(paged, 2)).Not.ToHaveAttributeAsync("data-selected", "");
        await rewinding.ScrollIntoViewIfNeededAsync();
        var onward = rewinding.Locator("[data-direction='next'] button");
        await onward.ClickAsync();
        await Expect(Slide(rewinding, 2)).ToHaveAttributeAsync("data-selected", "");
        await onward.ClickAsync();
        await Expect(Slide(rewinding, 3)).ToHaveAttributeAsync("data-selected", "");
        // At the end the next arrow stays, and takes it back to the first slide.
        await Expect(onward).ToBeEnabledAsync();
        await onward.ClickAsync();
        await Expect(Slide(rewinding, 1)).ToHaveAttributeAsync("data-selected", "");
        await disabled.ScrollIntoViewIfNeededAsync();
        await Expect(disabled.Locator("button:disabled")).ToHaveCountAsync(5);
    });

    private static ILocator Slide(ILocator carousel, int place) => carousel.Locator("[data-ui-carousel-slide]").Nth(place - 1);

    private async Task<ILocator> OpenAsync(string example)
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Data display");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data display",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        var carousel = Page.Locator($"[data-testid='ui-carousel-{example}'] [data-ui-carousel]");
        await Expect(carousel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await carousel.ScrollIntoViewIfNeededAsync();
        return carousel;
    }
}
