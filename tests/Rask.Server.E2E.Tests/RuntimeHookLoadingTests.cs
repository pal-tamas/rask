using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The behaviour hooks are a bundle of their own that a page loads when it first asks for one: never on a
///     page that asks for none, once on a page that does, and without losing what a reader did while it was on
///     its way.
/// </summary>
/// <remarks>
///     <para>
///         Three ways a hooked attribute reaches a page, and one request for each: it was in the first response
///         (the server writes the bundle's tag after the runtime's, so the two run back to back), it arrived in a
///         render, or it arrived with a navigation.
///     </para>
///     <para>
///         The late cases hold the bundle back on purpose (<see cref="HoldBundleAsync" />), because on loopback it
///         arrives before a test can do anything: what is proven is what a reader on a slow line gets.
///     </para>
/// </remarks>
public sealed class RuntimeHookLoadingTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string HoldMs = "700";

    [Fact]
    public async Task A_page_that_asks_for_no_hook_never_requests_the_hooks_bundle()
    {
        await using var session = await HookSession.OpenAsync<LazyHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        await page.WaitForTimeoutAsync(300);

        Assert.Equal(0, await session.HookBundleRequestsAsync());
        Assert.Equal(0, await page.Locator("script[data-rask-hooks]").CountAsync());
    }

    [Fact]
    public async Task A_page_rendered_with_a_hooked_attribute_carries_the_bundle_in_its_first_response_and_requests_it_once()
    {
        await using var session = await HookSession.OpenAsync<ModalAtLoadPage>(playwright);
        var page = session.Page;

        var firstResponse = await (await page.APIRequest.GetAsync(session.BaseUrl + "/")).TextAsync();
        await session.HooksLoadedAsync();

        Assert.Matches("<script src=\"/rask/rask\\.js\\?v=([0-9a-f]{16})\"></script><script src=\"/rask/rask-hooks\\.js\\?v=\\1\" data-rask-hooks data-rask-managed></script></body>", firstResponse);
        Assert.Equal(1, await session.HookBundleRequestsAsync());
        Assert.Equal(1, await page.Locator("script[data-rask-hooks]").CountAsync());
    }

    [Fact]
    public async Task A_hooked_attribute_that_arrives_in_a_render_loads_the_bundle_exactly_once()
    {
        await using var session = await HookSession.OpenAsync<LazyHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#show-tip");
        await session.HooksLoadedAsync();
        await page.ClickAsync("#show-otp");
        await Expect(page.Locator("#otp")).ToBeVisibleAsync();
        await page.HoverAsync("#tip-trigger");

        Assert.True(await session.ShownAsync("#bubble"));
        Assert.Equal(1, await session.HookBundleRequestsAsync());
        Assert.Equal(1, await page.Locator("script[data-rask-hooks]").CountAsync());
    }

    [Fact]
    public async Task A_hooked_attribute_that_arrives_with_a_navigation_loads_the_bundle_exactly_once()
    {
        await using var session = await HookSession.OpenAsync<LazyHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#go");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/hooked");
        await session.HooksLoadedAsync();
        await page.ClickAsync("#back");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        await page.ClickAsync("#go");
        await Expect(page.Locator("#routed-trigger")).ToBeVisibleAsync();
        await page.HoverAsync("#routed-trigger");

        Assert.True(await session.ShownAsync("#routed-bubble"));
        Assert.Equal(1, await session.HookBundleRequestsAsync());
    }

    [Fact]
    public async Task The_bundle_is_served_like_the_runtime_and_kept_for_a_year_under_the_runtimes_own_version()
    {
        await using var session = await HookSession.OpenAsync<ModalAtLoadPage>(playwright);
        var page = session.Page;

        var src = await page.Locator("script[data-rask-hooks]").GetAttributeAsync("src");
        var named = await page.APIRequest.GetAsync(session.BaseUrl + src);
        var bare = await page.APIRequest.GetAsync(session.BaseUrl + "/rask/rask-hooks.js");

        Assert.Equal("public, max-age=31536000, immutable", named.Headers["cache-control"]);
        Assert.Equal("no-cache", bare.Headers["cache-control"]);
        Assert.StartsWith("text/javascript", named.Headers["content-type"], StringComparison.Ordinal);
        Assert.Equal("nosniff", named.Headers["x-content-type-options"]);
    }

    [Fact]
    public async Task A_tooltip_hovered_before_its_hook_has_arrived_shows_when_it_does()
    {
        await using var session = await HookSession.OpenAsync<LazyHookPage>(playwright, beforeLoad: HoldBundleAsync);
        var page = session.Page;

        await page.ClickAsync("#show-tip");
        await page.HoverAsync("#tip-trigger");
        var whileOnItsWay = await session.ShownAsync("#bubble");
        await session.HooksLoadedAsync();

        Assert.False(whileOnItsWay);
        Assert.True(await session.ShownAsync("#bubble"));
        Assert.Equal("true", await page.GetAttributeAsync("#tip-trigger", "aria-expanded"));
    }

    [Fact]
    public async Task A_tooltip_the_pointer_left_before_its_hook_arrived_stays_hidden()
    {
        await using var session = await HookSession.OpenAsync<LazyHookPage>(playwright, beforeLoad: HoldBundleAsync);
        var page = session.Page;

        await page.ClickAsync("#show-tip");
        await page.HoverAsync("#tip-trigger");
        await page.Mouse.MoveAsync(900, 600);
        await session.HooksLoadedAsync();

        Assert.False(await session.ShownAsync("#bubble"));
    }

    [Fact]
    public async Task The_first_character_typed_into_a_code_before_its_hook_arrived_is_kept_and_moves_on_under_a_slow_cpu()
    {
        await using var session = await HookSession.OpenAsync<LazyHookPage>(playwright, beforeLoad: HoldBundleAsync);
        var page = session.Page;
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 8 });

        await page.ClickAsync("#show-otp");
        await page.Locator("#otp input[aria-label='1 of 3']").FocusAsync();
        await page.Keyboard.TypeAsync("1");
        await session.HooksLoadedAsync();
        await page.Keyboard.TypeAsync("23");

        await Expect(page.Locator("#code")).ToHaveTextAsync("code=123", new() { Timeout = 15_000 });
        Assert.Equal("1|2|3", await page.EvaluateAsync<string>("() => [...document.querySelectorAll('#otp input:not([type=hidden])')].map(c => c.value).join('|')"));
    }

    [Fact]
    public async Task The_first_character_typed_into_a_code_that_was_on_the_page_at_load_moves_on_under_a_slow_cpu()
    {
        await using var session = await HookSession.OpenAsync<OtpAtLoadPage>(playwright);
        var page = session.Page;
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 8 });

        await page.Locator("#otp input[aria-label='1 of 3']").FocusAsync();
        await page.Keyboard.TypeAsync("123");

        await Expect(page.Locator("#code")).ToHaveTextAsync("code=123", new() { Timeout = 15_000 });
        Assert.Equal("3 of 3", await page.EvaluateAsync<string>("() => document.activeElement.getAttribute('aria-label')"));
        Assert.Equal(1, await session.HookBundleRequestsAsync());
    }

    [Fact]
    public async Task A_dialog_rendered_open_is_a_modal_by_the_time_the_page_has_been_read()
    {
        await using var session = await HookSession.OpenAsync<ModalAtLoadPage>(playwright, beforeLoad: RecordAtPageReadAsync);
        var page = session.Page;

        var atPageRead = await page.EvaluateAsync<string>("() => window.__atPageRead");
        var atFirstFrame = await page.EvaluateAsync<string>("() => window.__atFirstFrame");

        // Both scripts are in the response and block the parser, so by DOMContentLoaded the hook has run.
        Assert.Equal("modal=true checked=true", atPageRead);
        // The frame after is on screen with it. (A frame painted BEFORE the scripts arrived cannot be ruled
        // out on a slow line; it could not when the hooks were part of the runtime either.)
        Assert.Equal("modal=true checked=true", atFirstFrame);
        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#dialog').hasAttribute('data-open')"));
    }

    [Fact]
    public async Task A_dialog_that_a_render_opens_on_a_page_with_no_hooks_becomes_a_modal_when_the_bundle_arrives()
    {
        await using var session = await HookSession.OpenAsync<LazyHookPage>(playwright, beforeLoad: HoldBundleAsync);
        var page = session.Page;

        await page.ClickAsync("#show-dialog");
        await session.HooksLoadedAsync();

        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#late-dialog').matches(':modal')"));
        Assert.Equal(1, await session.HookBundleRequestsAsync());
    }

    [Fact]
    public async Task A_render_after_the_bundle_ran_neither_removes_its_tag_nor_asks_for_it_again()
    {
        await using var session = await HookSession.OpenAsync<ModalAtLoadPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#close");
        await Expect(page.Locator("#dialog")).ToBeHiddenAsync();
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=2");

        Assert.Equal(1, await page.Locator("script[data-rask-hooks]").CountAsync());
        Assert.Equal(1, await session.HookBundleRequestsAsync());
    }

    // The bundle, a slow line's worth late.
    private static Task HoldBundleAsync(IPage page) =>
        page.RouteAsync("**/rask/rask-hooks.js*", async route =>
        {
            await Task.Delay(int.Parse(HoldMs, System.Globalization.CultureInfo.InvariantCulture));
            await route.ContinueAsync();
        });

    // What the page looked like when the parser finished, and in the first frame after that. localStorage is
    // per origin and the origin is a fresh port, so the key is written before any script of the page runs.
    private static Task RecordAtPageReadAsync(IPage page) =>
        page.AddInitScriptAsync("""
            localStorage.setItem('test-lazy-collapsed', 'true');
            const look = () => 'modal=' + !!document.querySelector('#dialog:modal') + ' checked=' + document.querySelector('#collapsed').checked;
            document.addEventListener('DOMContentLoaded', () => {
                window.__atPageRead = look();
                requestAnimationFrame(() => { window.__atFirstFrame = look(); });
            });
            """);
}

/// <summary>A page with no hooked attribute until a press or a navigation renders one.</summary>
public sealed partial class LazyHookPage(RouteState route) : Component
{
    private const string Tip = """
        <span id="tip" data-rask-tooltip="bubble" style="position:fixed;left:20px;top:200px">
          <button id="tip-trigger" type="button" aria-expanded="false" aria-controls="bubble">?</button>
          <div id="bubble" popover="manual" role="tooltip" style="position:fixed;left:20px;top:240px">Help</div>
        </span>
        """;

    private const string RoutedTip = """
        <span data-rask-tooltip="routed-bubble" style="position:fixed;left:20px;top:200px">
          <button id="routed-trigger" type="button">?</button>
          <div id="routed-bubble" popover="manual" role="tooltip" style="position:fixed;left:20px;top:240px">Help</div>
        </span>
        """;

    private int _count;
    private bool _tip;
    private bool _otp;
    private bool _dialog;
    private string _code = string.Empty;

    protected override Component? HeadAssets => Markup.Title["lazy hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("counted")[$"count={_count}"],
        P.Id("path")[$"path={route.Path}"],
        P.Id("code")[$"code={_code}"],
        Button.Id("count").OnClick(() => _count++)["count"],
        Button.Id("show-tip").OnClick(() => _tip = true)["tip"],
        Button.Id("show-otp").OnClick(() => _otp = true)["otp"],
        Button.Id("show-dialog").OnClick(() => _dialog = true)["dialog"],
        A.Id("go").Href("/hooked")["go"],
        A.Id("back").Href("/")["back"],
        Div.Id("tip-slot")[_tip ? Raw.Value(Tip) : null],
        Div.Id("routed-slot")[string.Equals(route.Path, "/hooked", StringComparison.Ordinal) ? Raw.Value(RoutedTip) : null],
        Div.Id("otp-slot")[
            _otp
                ? Div.Id("otp").Data("rask-otp", "numeric")[
                    Input.Value(_code).Type(InputType.Hidden).OnInput(v => _code = v ?? string.Empty),
                    Input.Of<string>().AriaLabel("1 of 3"),
                    Input.Of<string>().AriaLabel("2 of 3"),
                    Input.Of<string>().AriaLabel("3 of 3")
                ]
                : null
        ],
        Div.Id("dialog-slot")[
            _dialog ? Dialog.Id("late-dialog").Attributes(("data-rask-modal-open", "true"))["Late"] : null
        ]
    ];
}

/// <summary>A page whose first response already has a dialog to open and a checkbox to restore.</summary>
public sealed partial class ModalAtLoadPage : Component
{
    private int _count;
    private bool _open = true;

    protected override Component? HeadAssets => Markup.Title["hooks at load"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("counted")[$"count={_count}"],
        Button.Id("count").OnClick(() => _count++)["count"],
        Div[Raw.Value("""<input id="collapsed" type="checkbox" data-rask-persist="test-lazy-collapsed">""")],
        Dialog.Id("dialog").Attributes(("data-rask-modal-open", _open ? "true" : "false"))[
            Button.Id("close").OnClick(() => _open = false)["Close"]
        ]
    ];
}

/// <summary>A page whose first response already has a one-time code.</summary>
public sealed partial class OtpAtLoadPage : Component
{
    private string _code = string.Empty;

    protected override Component? HeadAssets => Markup.Title["code at load"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("code")[$"code={_code}"],
        Div.Id("otp").Data("rask-otp", "numeric")[
            Input.Value(_code).Type(InputType.Hidden).OnInput(v => _code = v ?? string.Empty),
            Input.Of<string>().AriaLabel("1 of 3"),
            Input.Of<string>().AriaLabel("2 of 3"),
            Input.Of<string>().AriaLabel("3 of 3")
        ]
    ];
}
