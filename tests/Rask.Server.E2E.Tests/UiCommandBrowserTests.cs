using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Command</c> on a live SERVER page: the palette is narrowed by typing, walked by the arrows and the pointer
///     and left in the browser, and the server hears only the command that is pressed — as Flux's palette asks its
///     server nothing until then.
/// </summary>
public sealed class UiCommandBrowserTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Box = "#palette input";

    private const string Lit = "() => [...document.querySelectorAll('[role=option][data-active]')].map(o => o.dataset.raskMatch).join(', ')";

    private const string Named = "() => document.getElementById(document.querySelector('#palette input').getAttribute('aria-activedescendant') || '')?.dataset.raskMatch || ''";

    private const string Shown = "() => [...document.querySelectorAll('[role=option]')].filter(o => !o.hasAttribute('data-hidden')).map(o => o.dataset.raskMatch).join(', ')";

    [Fact]
    public async Task Focusing_typing_walking_and_leaving_a_palette_sends_the_server_nothing()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync(Box);
        var focused = await page.EvaluateAsync<string>(Lit);
        await page.Keyboard.TypeAsync("g");
        var narrowed = await page.EvaluateAsync<string>(Shown);
        await page.Keyboard.PressAsync("ArrowDown");
        var walked = await page.EvaluateAsync<string>(Named);
        await page.HoverAsync("[role=option]:not([data-hidden]) >> nth=0");
        await page.Mouse.MoveAsync(2, 2);
        await page.Keyboard.PressAsync("Tab");

        Assert.Equal("New invoice", focused);
        Assert.Equal("Réglages, Sign out", narrowed);
        Assert.Equal("Sign out", walked);
        Assert.Equal("", await page.EvaluateAsync<string>(Lit));
        Assert.Equal((0, 0), watched.Frames());
    }

    [Fact]
    public async Task The_arrows_light_the_commands_shown_and_stop_at_the_ends()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync(Box);

        var lit = new List<string>();
        foreach (var key in new[] { "ArrowUp", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowUp" })
        {
            await page.Keyboard.PressAsync(key);
            lit.Add(await page.EvaluateAsync<string>(Lit));
        }

        Assert.Equal(["New invoice", "Archive", "Réglages", "Sign out", "Sign out", "Réglages"], lit);
        Assert.Equal("Réglages", await page.EvaluateAsync<string>(Named));
        Assert.Equal((0, 0), watched.Frames());
    }

    [Fact]
    public async Task A_query_matches_without_accents_and_says_so_when_nothing_is_left()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync(Box);

        await page.Keyboard.TypeAsync("REGL");
        var accents = await page.EvaluateAsync<string>(Shown);
        await page.Keyboard.TypeAsync("zzz");

        Assert.Equal("Réglages", accents);
        await Expect(page.GetByText("No results found")).ToBeVisibleAsync();
        await Expect(page.Locator("#palette [aria-label='Clear command input']")).ToBeVisibleAsync();
        Assert.Equal("", await page.EvaluateAsync<string>(Lit));
        Assert.Equal((0, 0), watched.Frames());
    }

    [Fact]
    public async Task Enter_presses_the_lit_command_which_is_the_pages_own_action_and_the_box_starts_over()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync(Box);
        await page.Keyboard.TypeAsync("sign");

        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("#ran")).ToHaveTextAsync("Sign out");
        Assert.Equal(1, watched.Frames().Sent);
        await Expect(page.Locator(Box)).ToHaveValueAsync("");
        Assert.Equal("New invoice, Archive, Réglages, Sign out", await page.EvaluateAsync<string>(Shown));
        Assert.Equal("New invoice", await page.EvaluateAsync<string>(Lit));
    }

    [Fact]
    public async Task The_clear_button_empties_the_box_and_every_command_is_back()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync(Box);
        await page.Keyboard.TypeAsync("sign");

        await page.ClickAsync("#palette [aria-label='Clear command input']");

        await Expect(page.Locator(Box)).ToHaveValueAsync("");
        Assert.Equal("New invoice, Archive, Réglages, Sign out", await page.EvaluateAsync<string>(Shown));
        Assert.Equal((0, 0), watched.Frames());
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

/// <summary>A palette of four commands on a Server page.</summary>
public sealed partial class CommandBrowserPage : Component
{
    private string _ran = "";

    protected override Component? HeadAssets => [Markup.Title["a command palette"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div.Style("width:480px;margin:16px")[
            P.Id("ran")[_ran],
            Div.Id("palette")[Ui.Command[
                Ui.CommandInput.Placeholder("Search..."),
                Ui.CommandItems[
                    Ui.CommandItem.OnClick(() => _ran = "New invoice")["New invoice"],
                    Ui.CommandItem["Archive"],
                    Ui.CommandItem.OnClick(() => _ran = "Réglages")["Réglages"],
                    Ui.CommandItem.OnClick(() => _ran = "Sign out")["Sign out"]
                ]
            ]]
        ];
}
