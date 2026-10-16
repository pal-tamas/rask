using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Command</c> on a live SERVER page: the palette is opened, narrowed by typing, walked by the arrows and
///     closed in the browser, and the server hears only the command that is pressed — as Flux's palette asks its
///     server nothing until then.
/// </summary>
public sealed class UiCommandBrowserTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Lit = "() => document.getElementById(document.querySelector('[role=combobox]').getAttribute('aria-activedescendant') || '')?.dataset.raskMatch || ''";

    private const string Shown = "() => [...document.querySelectorAll('dialog [role=option]')].filter(o => !o.parentElement.hidden).map(o => o.dataset.raskMatch).join(', ')";

    [Fact]
    public async Task Opening_typing_walking_and_closing_a_palette_sends_the_server_nothing()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync("#palette > button");
        var opened = await page.EvaluateAsync<string>(Lit);
        await page.Keyboard.TypeAsync("r");
        var narrowed = await page.EvaluateAsync<string>(Shown);
        await page.Keyboard.PressAsync("ArrowDown");
        var walked = await page.EvaluateAsync<string>(Lit);
        await page.Keyboard.PressAsync("Escape");

        // "Archive" matches and cannot be picked: it is shown, and the highlight passes over it.
        Assert.Equal("New invoice", opened);
        Assert.Equal("Archive, Réglages", narrowed);
        Assert.Equal("Réglages", walked);
        await Expect(page.Locator("dialog")).Not.ToBeVisibleAsync();
        Assert.Equal((0, 0), watched.Frames());
    }

    [Fact]
    public async Task The_arrows_pass_over_a_command_that_cannot_be_picked_and_go_around_the_ends()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync("#palette > button");

        var lit = new List<string>();
        foreach (var key in new[] { "ArrowDown", "ArrowDown", "ArrowDown", "ArrowUp" })
        {
            await page.Keyboard.PressAsync(key);
            lit.Add(await page.EvaluateAsync<string>(Lit));
        }

        Assert.Equal(["Réglages", "Sign out", "New invoice", "Sign out"], lit);
        await Expect(page.Locator("dialog [role=option][aria-selected=true]")).ToHaveCountAsync(1);
        Assert.Equal((0, 0), watched.Frames());
    }

    [Fact]
    public async Task A_query_matches_without_accents_hides_the_separator_and_says_so_when_nothing_is_left()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync("#palette > button");

        await page.Keyboard.TypeAsync("REGL");
        var accents = await page.EvaluateAsync<string>(Shown);
        var separator = page.Locator("dialog [role=separator]");
        await Expect(separator).ToBeHiddenAsync();
        await page.Keyboard.TypeAsync("zzz");

        Assert.Equal("Réglages", accents);
        await Expect(page.Locator("dialog .ui-command-empty")).ToBeVisibleAsync();
        Assert.Equal("", await page.EvaluateAsync<string>(Lit));
        Assert.Equal((0, 0), watched.Frames());
    }

    [Fact]
    public async Task Enter_presses_the_lit_command_which_is_the_pages_own_action_and_the_palette_starts_over()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync("#palette > button");
        await page.Keyboard.TypeAsync("sign");

        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("#ran")).ToHaveTextAsync("Sign out");
        await Expect(page.Locator("dialog")).Not.ToBeVisibleAsync();
        Assert.Equal(1, watched.Frames().Sent);
        await page.ClickAsync("#palette > button");
        await Expect(page.Locator("dialog input")).ToHaveValueAsync("");
        Assert.Equal("New invoice, Archive, Réglages, Sign out", await page.EvaluateAsync<string>(Shown));
        Assert.Equal("New invoice", await page.EvaluateAsync<string>(Lit));
    }

    // A session whose socket is counted from the moment the page and its hooks are in.
    private sealed class Watched(HookSession session, int[] counts) : IAsyncDisposable
    {
        private readonly int _sentBefore = Volatile.Read(ref counts[0]);
        private readonly int _receivedBefore = Volatile.Read(ref counts[1]);

        public HookSession Session => session;

        public static async Task<Watched> OpenAsync(PlaywrightFixture playwright)
        {
            var counts = new int[2];
            var session = await HookSession.OpenAsync<CommandBrowserPage>(playwright, beforeLoad: page =>
            {
                page.WebSocket += (_, socket) =>
                {
                    socket.FrameSent += (_, _) => Interlocked.Increment(ref counts[0]);
                    socket.FrameReceived += (_, _) => Interlocked.Increment(ref counts[1]);
                };
                return Task.CompletedTask;
            });
            await session.HooksLoadedAsync();
            // Whatever the load itself exchanged has gone by: from here on the socket is watched.
            await session.Page.WaitForTimeoutAsync(300);
            return new Watched(session, counts);
        }

        /// <summary>Frames sent and received since the page settled.</summary>
        public (int Sent, int Received) Frames() =>
            (Volatile.Read(ref counts[0]) - _sentBefore, Volatile.Read(ref counts[1]) - _receivedBefore);

        public ValueTask DisposeAsync() => session.DisposeAsync();
    }
}

/// <summary>A palette of four commands, one of which cannot be picked, on a Server page.</summary>
public sealed partial class CommandBrowserPage : Component
{
    private string _ran = "";

    protected override Component? HeadAssets => [Markup.Title["a command palette"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div.Style("width:480px;margin:16px")[
            P.Id("ran")[_ran],
            Div.Id("palette")[Ui.Command.Label("Search commands")[
                Ui.MenuItem.OnClick(() => _ran = "New invoice")["New invoice"],
                Ui.MenuItem.Disabled(true)["Archive"],
                Ui.MenuSeparator,
                Ui.MenuItem.OnClick(() => _ran = "Réglages")["Réglages"],
                Ui.MenuItem.Danger.OnClick(() => _ran = "Sign out")["Sign out"]
            ]]
        ];
}
