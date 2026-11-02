using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's NATIVE FEEL (owner, 2026-11-02) on the showcase, on a desktop and on a phone: a control selects no
///     text and waits for no second tap, the page's content stays text, a picture that is chrome does not drag, an
///     overlay keeps its scroll, and no field is small enough for iOS to zoom the page into it on a tap.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitNativeFeelTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    // The first of each on its page: the selector, and the page it is on.
    private static readonly (string Slug, string Heading, string Selector)[] Chrome =
    [
        ("button", "Button", "[data-ui-button]"),
        ("tabs", "Tabs", "[data-ui-tab]"),
        ("navlist", "Navlist", "[data-ui-navlist-item]"),
        ("dropdown", "Dropdown", "[role^='menuitem']"),
        ("button", "Button", "[data-ui-sidebar-toggle]"),
    ];

    private static readonly (string Slug, string Heading, string Selector)[] Content =
    [
        ("table", "Table", "[data-ui-cell]"),
        ("text", "Text", "[data-ui-text]"),
        ("heading", "Heading", "[data-ui-heading]"),
        ("button", "Button", "main pre code"),
    ];

    // Each part a reader types into, by its page: the first one there, which is each page's default size.
    private static readonly (string Slug, string Heading, string Selector)[] Fields =
    [
        ("input", "Input", "[data-ui-input] input:not([type=hidden])"),
        ("textarea", "Textarea", "textarea"),
        ("select", "Select", "select, [data-ui-select-button]"),
        ("otp-input", "OTP input", "[data-ui-otp-input]"),
        ("phone", "Phone", "[data-ui-phone-input]"),
        ("date-picker", "Date picker", "[data-ui-date-picker-button]"),
        ("time-picker", "Time picker", "[data-ui-time-picker-button]"),
        ("autocomplete", "Autocomplete", "main input[role='combobox']"),
        ("editor", "Rich text editor", "[contenteditable='true']"),
        ("command", "Command palette", "[data-ui-command-input] input"),
    ];

    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task On_a_desktop_a_control_selects_nothing_and_the_content_stays_text() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(1920, 1080);

        await AssertChromeAndContentAsync();
    });

    [Fact]
    public Task On_a_phone_a_control_selects_nothing_and_the_content_stays_text() => RunAsync(async () =>
    {
        await OnAPhoneAsync(AssertChromeAndContentAsync);
    });

    [Fact]
    public Task A_triple_click_selects_a_cells_text_and_a_drag_across_buttons_selects_nothing() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(1920, 1080);
        await OpenUiPageAsync("table", "Table");
        var cell = Page.Locator("[data-ui-cell]").First;
        await cell.ScrollIntoViewIfNeededAsync();
        var text = (await cell.InnerTextAsync()).Trim();

        await cell.ClickAsync(new LocatorClickOptions { ClickCount = 3 });
        var selected = await Page.EvaluateAsync<string>("() => getSelection().toString()");

        Assert.NotEqual("", text);
        Assert.Contains(text, selected, StringComparison.Ordinal);

        await OpenUiPageAsync("button", "Button");
        var group = Page.Locator("[data-ui-button-group]").First;
        await group.ScrollIntoViewIfNeededAsync();
        var buttons = group.Locator("[data-ui-button]");
        var first = (await buttons.First.BoundingBoxAsync())!;
        var last = (await buttons.Last.BoundingBoxAsync())!;

        await Page.EvaluateAsync("() => getSelection().removeAllRanges()");
        await Page.Mouse.MoveAsync(first.X + 4, first.Y + (first.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(last.X + last.Width - 4, last.Y + (last.Height / 2), new MouseMoveOptions { Steps = 8 });
        await Page.Mouse.UpAsync();

        Assert.True(await buttons.CountAsync() > 1);
        Assert.Equal("", (await Page.EvaluateAsync<string>("() => getSelection().toString()")).Trim());
    });

    [Fact]
    public Task A_modal_keeps_its_scroll_and_a_picture_that_is_chrome_does_not_drag() => RunAsync(async () =>
    {
        await OpenUiPageAsync("modal", "Modal");
        var overscroll = await Style("[data-ui-modal] > dialog", "overscrollBehaviorY");

        await OpenUiPageAsync("avatar", "Avatar");
        var avatar = await Page.Locator("[data-ui-avatar] img").First.GetAttributeAsync("draggable");
        var avatarDrag = await Style("[data-ui-avatar] img", "webkitUserDrag");

        await OpenUiPageAsync("brand", "Brand");
        var brand = await Page.Locator("[data-ui-brand] img").First.GetAttributeAsync("draggable");
        var link = await Page.Locator("[data-ui-brand]").First.GetAttributeAsync("draggable");

        Assert.Equal("contain", overscroll);
        Assert.Equal("false", avatar);
        Assert.Equal("none", avatarDrag);
        Assert.Equal("false", brand);
        Assert.Null(link);
    });

    [Fact]
    public Task On_a_desktop_every_field_is_drawn_at_fourteen_pixels() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(1920, 1080);

        var sizes = await FieldSizesAsync();

        Assert.All(sizes, size => Assert.True(size.Px == 14, $"{size.Slug}: {size.Px}px at 1920"));
    });

    [Fact]
    public Task On_a_phone_no_field_is_small_enough_for_ios_to_zoom_into() => RunAsync(async () =>
    {
        var sizes = new List<(string Slug, double Px)>();

        await OnAPhoneAsync(async () => sizes.AddRange(await FieldSizesAsync()));

        Assert.Equal(Fields.Length, sizes.Count);
        Assert.All(sizes, size => Assert.True(size.Px >= 16, $"{size.Slug}: {size.Px}px at 390"));
    });

    private async Task AssertChromeAndContentAsync()
    {
        foreach (var (slug, heading, selector) in Chrome)
        {
            await OpenUiPageAsync(slug, heading);

            Assert.Equal("none", await Style(selector, "userSelect"));
            Assert.Equal("manipulation", await Style(selector, "touchAction"));
        }

        foreach (var (slug, heading, selector) in Content)
        {
            await OpenUiPageAsync(slug, heading);

            Assert.NotEqual("none", await Style(selector, "userSelect"));
            Assert.Equal("auto", await Style(selector, "touchAction"));
        }

        await OpenUiPageAsync("toast", "Toast");
        await Page.Locator("#toast-success").ClickAsync();
        var toast = "[data-ui-toast] > [data-ui-toast-dialog]";
        await Expect(Page.Locator(toast)).ToContainTextAsync("Post created", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        Assert.NotEqual("none", await Style(toast, "userSelect"));
        Assert.Equal("none", await Style(toast + " button", "userSelect"));
    }

    private async Task<List<(string Slug, double Px)>> FieldSizesAsync()
    {
        var sizes = new List<(string, double)>();
        foreach (var (slug, heading, selector) in Fields)
        {
            await OpenUiPageAsync(slug, heading);
            var size = await Style(selector, "fontSize");
            sizes.Add((slug, double.Parse(size.Replace("px", "", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture)));
        }

        return sizes;
    }

    // The computed value of `property` on the first element `selector` matches.
    private async Task<string> Style(string selector, string property)
    {
        var element = Page.Locator(selector).First;
        await Expect(element).ToBeAttachedAsync(new LocatorAssertionsToBeAttachedOptions { Timeout = 15_000 });
        return await element.EvaluateAsync<string>("(el, key) => getComputedStyle(el)[key]", property);
    }

    // A phone's screen — touch and a phone's viewport — for the length of `body`, then the desktop page back.
    private async Task OnAPhoneAsync(Func<Task> body)
    {
        var desktop = Page;
        await using var phone = await pw.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            IsMobile = true,
            HasTouch = true,
        });
        await phone.RouteAsync("**://fonts.googleapis.com/**", route => route.AbortAsync());
        await phone.RouteAsync("**://fonts.gstatic.com/**", route => route.AbortAsync());
        Page = await phone.NewPageAsync();
        try
        {
            await body();
        }
        finally
        {
            Page = desktop;
        }
    }
}
