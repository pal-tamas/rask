using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Dropdown</c>, <c>Ui.Menu</c> and <c>Ui.Context</c> on a live SERVER page, under a pointer and a
///     keyboard: the attributes the kit writes and the hooks that act on them — in the browser, with nothing
///     sent over the socket while the reader moves about an open menu.
/// </summary>
/// <remarks>
///     Every expectation is what Flux UI's live dropdown, context and sidebar-demo pages did on 2026-10-09, read
///     as three things after each step — the rows carrying <c>data-active</c>, where focus is, which flyouts are
///     showing:
///     <list type="bullet">
///         <item>opened by a press: no row lit, focus on the menu, <c>&lt;html&gt;</c> locked (a dropdown and a
///         context menu both); a menu the pointer opened (the rail's) locks nothing and takes no focus;</item>
///         <item>the pointer lights the row it enters and focus does not move; the first arrow then lands ON
///         that row, and later arrows continue from whichever row is lit;</item>
///         <item>a row the keyboard lit stays lit when the pointer leaves the menu; one the pointer lit does
///         not;</item>
///         <item>a flyout opens under the pointer (3 ms), stays through the diagonal to it and after the pointer
///         has left the menu altogether, and closes when another row is entered;</item>
///         <item>ArrowRight goes into a flyout with its row and the flyout's first row both lit.</item>
///     </list>
/// </remarks>
public sealed class UiMenuHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // "lit rows | focus | flyouts showing", for the dropdown under [data-testid=menu].
    private const string State = """
        () => {
            const menu = document.querySelector("[data-testid='menu'] [popover]");
            const words = el => el.textContent.trim();
            const rows = [...menu.querySelectorAll('[role^=menuitem]')];
            const active = document.activeElement;
            const focus = active === menu ? 'MENU' : rows.includes(active) ? words(active) : active.tagName;
            const flyouts = [...menu.querySelectorAll('[data-ui-menu-submenu]')]
                .filter(sub => sub.querySelector('[role=menu]').getClientRects().length > 0)
                .map(sub => words(sub.querySelector('[role=menuitem]')));
            return [rows.filter(row => row.hasAttribute('data-active')).map(words).join('+'), focus, flyouts.join('+')].join(' | ');
        }
        """;

    private const string Lock = "() => { const s = getComputedStyle(document.documentElement); return s.overflow + '|' + s.pointerEvents; }";

    [Fact]
    public async Task A_pressed_dropdown_and_a_context_menu_lock_the_page_and_a_hover_menu_locks_nothing_and_takes_no_focus()
    {
        await using var session = await HookSession.OpenAsync<MenuHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();

        await page.ClickAsync("#options");
        var underDropdown = await page.EvaluateAsync<string>(Lock);
        await page.Keyboard.PressAsync("Escape");
        var afterDropdown = await page.EvaluateAsync<string>(Lock);
        await page.ClickAsync("#area", new PageClickOptions { Button = MouseButton.Right });
        await Expect(page.Locator("[data-testid='context'] [popover]")).ToBeVisibleAsync();
        var underContext = await page.EvaluateAsync<string>(Lock);
        await page.Keyboard.PressAsync("Escape");
        await page.FocusAsync("#before");
        await page.HoverAsync("#hovered");
        await Expect(page.Locator("[data-testid='hover'] [popover]")).ToBeVisibleAsync();

        Assert.Equal("hidden|none", underDropdown);
        Assert.Equal("visible|auto", afterDropdown);
        Assert.Equal("hidden|none", underContext);
        Assert.Equal("visible|auto", await page.EvaluateAsync<string>(Lock));
        // Flux's rail menu: focus stayed on the button that had it.
        Assert.Equal("before", await session.FocusAsync());
    }

    [Fact]
    public async Task The_pointer_lights_a_row_without_taking_focus_and_the_first_arrow_lands_on_that_row()
    {
        await using var session = await HookSession.OpenAsync<MenuHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.ClickAsync("#options");
        await SeeAsync(page, " | MENU | ");

        await OverAsync(page, "Delete");
        await SeeAsync(page, "Delete | MENU | ");
        await OverAsync(page, "New post");
        await SeeAsync(page, "New post | MENU | ");
        await page.Keyboard.PressAsync("ArrowDown");
        await SeeAsync(page, "New post | New post | ");
        await page.Keyboard.PressAsync("ArrowDown");

        await SeeAsync(page, "Sort by | Sort by | ");
    }

    [Fact]
    public async Task The_arrows_continue_from_the_lit_row_and_only_a_row_the_pointer_lit_goes_dark_when_it_leaves()
    {
        await using var session = await HookSession.OpenAsync<MenuHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.ClickAsync("#options");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowDown");
        await SeeAsync(page, "Sort by | Sort by | ");

        // The pointer lights another row; focus stays where the keyboard put it.
        await OverAsync(page, "New post");
        await SeeAsync(page, "New post | Sort by | ");
        // The next arrow counts from the lit row, not from the focused one.
        await page.Keyboard.PressAsync("ArrowDown");
        await SeeAsync(page, "Sort by | Sort by | ");
        // The keyboard's row stays lit when the pointer goes.
        await page.Mouse.MoveAsync(900, 650, new MouseMoveOptions { Steps = 4 });
        await SeeAsync(page, "Sort by | Sort by | ");
        await OverAsync(page, "Delete");
        await SeeAsync(page, "Delete | Sort by | ");
        await page.Mouse.MoveAsync(900, 650, new MouseMoveOptions { Steps = 4 });

        await SeeAsync(page, " | Sort by | ");
    }

    [Fact]
    public async Task A_flyout_stays_through_the_diagonal_and_after_the_pointer_leaves_the_menu_and_closes_on_another_row()
    {
        await using var session = await HookSession.OpenAsync<MenuHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.ClickAsync("#options");
        var row = (await Row(page, "Sort by").BoundingBoxAsync())!;

        await OverAsync(page, "Sort by");
        await SeeAsync(page, "Sort by | MENU | Sort by");
        var flyout = (await page.Locator("[data-testid='menu'] [data-ui-menu-submenu] > [role=menu]").First.BoundingBoxAsync())!;
        // From the row to the foot of its flyout, across "Filter" and "Delete": neither is entered.
        for (var step = 1; step <= 12; step++)
        {
            await page.Mouse.MoveAsync(
                row.X + (row.Width / 2) + ((flyout.X + 8 - row.X - (row.Width / 2)) * step / 12),
                row.Y + (row.Height / 2) + ((flyout.Y + flyout.Height - 8 - row.Y - (row.Height / 2)) * step / 12));
        }

        await OverAsync(page, "Popularity");
        await SeeAsync(page, "Popularity | MENU | Sort by");
        // Out of the flyout and the menu altogether: Flux's flyout was still there 400 ms later.
        await page.Mouse.MoveAsync(900, 650, new MouseMoveOptions { Steps = 5 });
        await page.WaitForTimeoutAsync(400);
        await SeeAsync(page, " | MENU | Sort by");
        await OverAsync(page, "Delete");

        await SeeAsync(page, "Delete | MENU | ");
    }

    [Fact]
    public async Task ArrowRight_goes_into_a_flyout_with_its_row_still_lit_and_ArrowLeft_comes_back_out()
    {
        await using var session = await HookSession.OpenAsync<MenuHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.FocusAsync("#options");

        await page.Keyboard.PressAsync("ArrowDown");
        await SeeAsync(page, "New post | New post | ");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowRight");
        await SeeAsync(page, "Sort by+Name | Name | Sort by");
        await page.Keyboard.PressAsync("ArrowDown");
        await SeeAsync(page, "Sort by+Date | Date | Sort by");
        await page.Keyboard.PressAsync("ArrowLeft");
        await SeeAsync(page, "Sort by | Sort by | ");
        await page.Keyboard.PressAsync("Escape");

        await Expect(page.Locator("[data-testid='menu'] [popover]")).ToBeHiddenAsync();
        Assert.Equal("options", await session.FocusAsync());
    }

    [Fact]
    public async Task The_arrows_step_over_a_disabled_row_and_stop_at_the_ends_a_letter_jumps_and_Enter_picks()
    {
        await using var session = await HookSession.OpenAsync<MenuHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        const string focused = "() => document.activeElement.textContent.trim()";
        var walked = new List<string>();

        await page.ClickAsync("#more");
        foreach (var key in (string[])["ArrowDown", "ArrowDown", "ArrowDown", "ArrowDown", "End", "Home", "PageUp", "ArrowUp", "ArrowUp", "ArrowUp", "d", "s"])
        {
            if (key is "s")
            {
                // Letters typed within half a second are one prefix: "ds" is nobody's.
                await page.WaitForTimeoutAsync(650);
            }

            await page.Keyboard.PressAsync(key);
            walked.Add(await page.EvaluateAsync<string>(focused));
        }

        await page.Keyboard.PressAsync("Enter");

        // Flux, key for key: over "Duplicate", no wrapping at either end, and Home, End and the page keys are not
        // menu keys. `d` passes the disabled row that starts with it.
        Assert.Equal(["Edit", "Share", "Delete", "Delete", "Delete", "Delete", "Delete", "Share", "Edit", "Edit", "Delete", "Share"], walked);
        await Expect(page.Locator("#picked")).ToHaveTextAsync("picked share");
        await Expect(page.Locator("[data-testid='more'] [popover]")).ToBeHiddenAsync();
        Assert.Equal("more", await session.FocusAsync());
    }

    [Fact]
    public async Task A_pointer_gliding_over_every_row_and_flyout_and_the_arrow_keys_send_the_server_nothing()
    {
        var sent = 0;
        var received = 0;
        await using var session = await HookSession.OpenAsync<MenuHookPage>(playwright, beforeLoad: page =>
        {
            page.WebSocket += (_, socket) =>
            {
                socket.FrameSent += (_, _) => Interlocked.Increment(ref sent);
                socket.FrameReceived += (_, _) => Interlocked.Increment(ref received);
            };
            return Task.CompletedTask;
        });
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.ClickAsync("#options");
        await SeeAsync(page, " | MENU | ");
        // The toggle the page hears has gone and its answer has come: from here on the socket is watched.
        await page.WaitForTimeoutAsync(300);
        var (sentBefore, receivedBefore) = (Volatile.Read(ref sent), Volatile.Read(ref received));

        foreach (var row in (string[])["New post", "Sort by", "Filter", "Delete", "Filter", "Draft", "Published", "Sort by", "Name", "Date", "Popularity", "New post"])
        {
            await OverAsync(page, row);
        }

        await SeeAsync(page, "New post | MENU | ");
        foreach (var key in (string[])["ArrowDown", "ArrowDown", "ArrowRight", "ArrowDown", "ArrowLeft", "ArrowDown", "f", "ArrowUp"])
        {
            await page.Keyboard.PressAsync(key);
        }

        await SeeAsync(page, "Sort by | Sort by | ");
        await page.Mouse.MoveAsync(900, 650, new MouseMoveOptions { Steps = 4 });
        await page.WaitForTimeoutAsync(300);

        // Twelve rows entered, three flyouts opened and closed, eight keys: not one frame either way.
        Assert.Equal(0, Volatile.Read(ref sent) - sentBefore);
        Assert.Equal(0, Volatile.Read(ref received) - receivedBefore);
    }

    [Fact]
    public async Task What_the_pointer_and_the_keys_wrote_survives_a_render_the_server_sends_meanwhile()
    {
        await using var session = await HookSession.OpenAsync<MenuHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.ClickAsync("#options");
        await page.Keyboard.PressAsync("ArrowDown");
        await OverAsync(page, "Sort by");
        await OverAsync(page, "Date");
        await SeeAsync(page, "Date | New post | Sort by");

        // A press the page hears, on a row that keeps the menu open: it renders the menu again, words and all.
        await page.EvaluateAsync("() => document.getElementById('tick').click()");
        await Expect(page.Locator("#tick")).ToHaveTextAsync("Ticked 1");
        await page.EvaluateAsync("() => document.getElementById('tick').click()");
        await Expect(page.Locator("#tick")).ToHaveTextAsync("Ticked 2");

        // The lit row, the tab stop and the open flyout are the runtime's, and the render left them alone.
        Assert.Equal("Date | New post | Sort by", await page.EvaluateAsync<string>(State));
        Assert.Equal("0", await page.Locator("[data-testid='menu'] [role=menuitem]").First.GetAttributeAsync("tabindex"));
    }

    private static ILocator Row(IPage page, string words) =>
        page.Locator("[data-testid='menu'] [role^=menuitem]").Filter(new LocatorFilterOptions { HasTextString = words }).First;

    // A real pointer onto the row's middle, in steps: a jump would enter nothing on the way.
    private static async Task OverAsync(IPage page, string words)
    {
        var box = (await Row(page, words).BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2), new MouseMoveOptions { Steps = 4 });
    }

    // The hook answers at once and the page's own cursor a round trip later: this is where they agree.
    private static async Task SeeAsync(IPage page, string expected)
    {
        try
        {
            await page.WaitForFunctionAsync("expected => (" + State + ")() === expected", expected, new PageWaitForFunctionOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // The assertion below says what the page showed instead.
        }

        // And still, once the socket has gone quiet.
        await page.WaitForTimeoutAsync(150);
        Assert.Equal(expected, await page.EvaluateAsync<string>(State));
    }
}

/// <summary>A dropdown with two submenus, a hover dropdown and a context menu, on a page a session drives.</summary>
public sealed partial class MenuHookPage : Component
{
    // The kit's own sheet: it is what keeps a flyout shut until its row is pointed at or opened.
    protected override Component? HeadAssets => [Markup.Title["menu hooks"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    private int _ticks;
    private string _picked = "nothing";

    protected override Component? Render() =>
    [
        Button.Id("before")["Before"],
        // Outside the menu, and pressed from script while the menu is open: a render the menu did not ask for.
        Button.Id("tick").OnClick(() => _ticks++)[$"Ticked {_ticks}"],
        Div.Data("testid", "menu")[
            Ui.Dropdown[
                Button.Id("options")["Options"],
                Ui.Menu[
                    Ui.MenuItem["New post"],
                    Ui.MenuSubmenu.Heading("Sort by")[Ui.MenuItem["Name"], Ui.MenuItem["Date"], Ui.MenuItem["Popularity"]],
                    Ui.MenuSubmenu.Heading("Filter")[Ui.MenuItem["Draft"], Ui.MenuItem["Published"]],
                    Ui.MenuItem.Danger["Delete"]
                ]
            ]
        ],
        P.Id("picked")[$"picked {_picked}"],
        Div.Data("testid", "more")[
            Ui.Dropdown[
                Button.Id("more")["More"],
                Ui.Menu[
                    Ui.MenuItem.OnClick(() => _picked = "edit")["Edit"],
                    Ui.MenuItem.Disabled(true)["Duplicate"],
                    Ui.MenuItem.OnClick(() => _picked = "share")["Share"],
                    Ui.MenuItem.Danger.OnClick(() => _picked = "delete")["Delete"]
                ]
            ]
        ],
        Div.Data("testid", "hover")[
            Ui.Dropdown.Hover()[Button.Id("hovered")["Hover"], Ui.Menu[Ui.MenuItem["One"], Ui.MenuItem["Two"]]]
        ],
        Div.Data("testid", "context")[
            Ui.Context[Div.Id("area").Style("width:200px;height:80px")["Right click"], Ui.Menu[Ui.MenuItem["Copy"], Ui.MenuItem["Paste"]]]
        ],
        Div.Style("height:2000px")
    ];
}
