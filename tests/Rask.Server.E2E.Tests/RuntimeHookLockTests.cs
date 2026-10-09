using System.Text;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The page lock (<c>data-rask-lock</c>) leaves the page exactly where the reader had it: the same
///     <c>scrollY</c> and the same content box before, while and after an overlay is open.
/// </summary>
/// <remarks>
///     <para>
///         Measured on Flux UI's live dropdown, context, popover, date picker and modal pages on 2026-10-09, at
///         1920 × 1080 and 390 × 844, in a browser showing its scrollbars: <c>scrollY</c> the same before, during
///         and after; the content's <c>x</c> and <c>width</c> the same; <c>&lt;html&gt;</c> given
///         <c>overflow: hidden; pointer-events: none; scrollbar-gutter: stable</c> (a modal: no
///         <c>pointer-events</c>), and nothing on <c>&lt;body&gt;</c>.
///     </para>
///     <para>
///         What three integrations saw move was the gutter: <c>scrollbar-gutter: stable</c> reserves a
///         scrollbar's 15 px whether or not one was showing, and a browser run with its scrollbars hidden — every
///         headless one — had none. The page narrowed, its text wrapped again, and scroll anchoring moved
///         <c>scrollY</c> by the lines that added above the reader's place. So both browsers are driven here.
///     </para>
/// </remarks>
public sealed class RuntimeHookLockTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // scrollY, a paragraph's x and width, then what <html> computes to.
    private const string Place = """
        () => {
            const box = document.getElementById('probe').getBoundingClientRect();
            const html = getComputedStyle(document.documentElement);
            return [scrollY, box.x, box.width, html.overflow, html.pointerEvents, html.scrollbarGutter].join('|');
        }
        """;

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(390, 844)]
    public async Task A_page_with_no_scrollbar_showing_keeps_its_place_and_its_width_under_every_lock(int width, int height)
    {
        await using var session = await HookSession.OpenAsync<LockHookPage>(playwright, Viewport(width, height));
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.EvaluateAsync("() => document.getElementById('open').scrollIntoView({ block: 'center' })");

        var walk = await WalkAsync(page);

        Assert.True(walk.Before.ScrollY > 1000, "the page is not scrolled to its middle");
        Assert.Equal("visible|auto|auto", walk.Before.Html);
        // No gutter where no scrollbar took room: reserving one is what moved the page.
        Assert.Equal("hidden|none|auto", walk.Popover.Html);
        Assert.Equal("hidden|auto|auto", walk.Modal.Html);
        Assert.Equal("hidden|none|auto", walk.Nested.Html);
        Assert.Equal("hidden|auto|auto", walk.NestedClosed.Html);
        Assert.Equal("hidden|none|auto", walk.DialogPopover.Html);
        Assert.All(walk.All, step => Assert.Equal(walk.Before.Where, step.Where));
        Assert.Equal(walk.Before, walk.After);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(390, 844)]
    public async Task A_page_with_a_scrollbar_keeps_its_place_and_the_gutter_Flux_keeps_under_every_lock(int width, int height)
    {
        // The fixture's browser hides its scrollbars, as every headless one does. This one shows them.
        await using var browser = await playwright.Playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"] });
        await using var session = await HookSession.OpenAsync<LockHookPage>(playwright, Viewport(width, height), browser: browser);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.EvaluateAsync("() => document.getElementById('open').scrollIntoView({ block: 'center' })");
        var scrollbar = await page.EvaluateAsync<int>("() => innerWidth - document.documentElement.clientWidth");

        var walk = await WalkAsync(page);

        Assert.True(scrollbar > 0, "this browser shows no scrollbar either");
        Assert.True(walk.Before.ScrollY > 1000, "the page is not scrolled to its middle");
        Assert.Equal("visible|auto|auto", walk.Before.Html);
        Assert.Equal("hidden|none|stable", walk.Popover.Html);
        Assert.Equal("hidden|auto|stable", walk.Modal.Html);
        Assert.Equal("hidden|none|stable", walk.Nested.Html);
        Assert.Equal("hidden|auto|stable", walk.NestedClosed.Html);
        Assert.Equal("hidden|none|stable", walk.DialogPopover.Html);
        Assert.All(walk.All, step => Assert.Equal(walk.Before.Where, step.Where));
        Assert.Equal(walk.Before, walk.After);
    }

    [Fact]
    public async Task A_page_too_short_to_scroll_is_not_narrowed_by_a_gutter_it_never_had()
    {
        await using var browser = await playwright.Playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"] });
        await using var session = await HookSession.OpenAsync<ShortLockHookPage>(playwright, Viewport(1920, 1080), browser: browser);
        var page = session.Page;
        await session.HooksLoadedAsync();

        var before = Step.Parse(await page.EvaluateAsync<string>(Place));
        await page.ClickAsync("#open");
        var open = Step.Parse(await page.EvaluateAsync<string>(Place));
        await page.Keyboard.PressAsync("Escape");

        Assert.Equal("hidden|none|auto", open.Html);
        Assert.Equal(before.Where, open.Where);
        Assert.Equal(before, Step.Parse(await page.EvaluateAsync<string>(Place)));
    }

    private static BrowserNewContextOptions Viewport(int width, int height) =>
        new() { ViewportSize = new() { Width = width, Height = height } };

    // A popover; a modal with a popover inside it, closed inner first; a <dialog popover>.
    private static async Task<Walk> WalkAsync(IPage page)
    {
        async Task<Step> AfterAsync(Func<Task> act)
        {
            await act();
            // The count that settles a refused beforetoggle runs a task later.
            await page.WaitForTimeoutAsync(60);
            return Step.Parse(await page.EvaluateAsync<string>(Place));
        }

        var before = Step.Parse(await page.EvaluateAsync<string>(Place));
        var popover = await AfterAsync(() => page.ClickAsync("#open"));
        var closed = await AfterAsync(() => page.Keyboard.PressAsync("Escape"));
        var modal = await AfterAsync(() => page.ClickAsync("#modal-open"));
        var nested = await AfterAsync(() => page.ClickAsync("#inner-open"));
        var nestedClosed = await AfterAsync(() => page.Keyboard.PressAsync("Escape"));
        var modalClosed = await AfterAsync(() => page.Keyboard.PressAsync("Escape"));
        var dialogPopover = await AfterAsync(() => page.ClickAsync("#calendar-open"));
        var after = await AfterAsync(() => page.Keyboard.PressAsync("Escape"));

        return new Walk(before, popover, modal, nested, nestedClosed, dialogPopover, after,
            [popover, closed, modal, nested, nestedClosed, modalClosed, dialogPopover, after]);
    }

    private sealed record Walk(
        Step Before, Step Popover, Step Modal, Step Nested, Step NestedClosed, Step DialogPopover, Step After,
        IReadOnlyList<Step> All);

    /// <summary>Where the page is — its scroll offset and a paragraph's x and width — and what &lt;html&gt; computes to.</summary>
    private sealed record Step(double ScrollY, string Where, string Html)
    {
        public static Step Parse(string measured)
        {
            var parts = measured.Split('|');
            return new Step(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                string.Join('|', parts[..3]),
                string.Join('|', parts[3..]));
        }
    }
}

/// <summary>
///     A page of wrapping text, taller than any viewport, with a locking popover, a modal holding another, and
///     a <c>&lt;dialog popover&gt;</c> in its middle.
/// </summary>
public sealed partial class LockHookPage : Component
{
    internal const string Overlays = """
        <style>
          body { margin: 0; font: 14px/20px sans-serif } main { padding: 0 24px }
          #pop, #inner, #calendar { position: fixed; inset: auto; left: 40px; top: 200px; margin: 0 }
          #modal { position: fixed; inset: 0 0 0 auto; margin: 0; width: 280px; height: 100dvh; max-height: none }
        </style>
        <button id="open" type="button" popovertarget="pop">Open</button>
        <div id="pop" popover data-rask-lock>A select's list</div>
        <button id="modal-open" type="button" command="show-modal" commandfor="modal">Filters</button>
        <dialog id="modal" data-rask-modal="any" data-rask-lock="scroll">
          <button id="inner-open" type="button" popovertarget="inner">A select in the flyout</button>
          <div id="inner" popover data-rask-lock>Its list</div>
        </dialog>
        <button id="calendar-open" type="button" popovertarget="calendar">Date</button>
        <dialog id="calendar" popover data-rask-lock>A calendar</dialog>
        """;

    private static readonly string _text = Text();

    protected override Component? HeadAssets => Markup.Title["lock hook"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Main[
            Raw.Value(_text),
            P.Id("probe")["A paragraph whose place is measured."],
            Raw.Value(Overlays),
            Raw.Value(_text)
        ];

    // Sixty paragraphs of words of uneven length: narrowed by a scrollbar's width, some of them wrap again.
    private static string Text()
    {
        string[] words = ["The", "quick", "brown", "fox", "jumps", "over", "the", "lazy", "dog", "and", "keeps", "on", "running", "until", "evening"];
        var text = new StringBuilder();
        for (var paragraph = 0; paragraph < 60; paragraph++)
        {
            text.Append("<p>");
            for (var word = 0; word < 40 + (paragraph % 7) * 9; word++)
            {
                text.Append(words[(word * 7 + paragraph) % words.Length]).Append((word + paragraph) % 5 == 0 ? "ish " : " ");
            }

            text.Append("</p>");
        }

        return text.ToString();
    }
}

/// <summary>The same overlays on a page too short to scroll: no scrollbar, whatever the browser shows.</summary>
public sealed partial class ShortLockHookPage : Component
{
    protected override Component? HeadAssets => Markup.Title["lock hook, short"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Main[P.Id("probe")["A paragraph whose place is measured."], Raw.Value(LockHookPage.Overlays)];
}
