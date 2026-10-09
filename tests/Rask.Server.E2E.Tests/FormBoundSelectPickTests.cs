using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The reported form, on the host it was reported from: a routed page under a layout with the toasts and the
///     leave dialog, mapped under a path base through the endpoint-routing overload, in Development.
/// </summary>
/// <remarks>
///     <c>SelectPickKeepsTypingTests</c> holds a one-page app to a diff a pick. The app that reported the whole
///     page still measured 98 KB a pick on a build with that fix, so this suite is its shape and its host: a
///     multiple listbox bound to a list of ids between other bound fields, each with an error slot.
/// </remarks>
[Collection(PathBaseCollection.Name)]
public sealed class FormBoundSelectPickTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Trigger = "[data-ui-select-button]";

    private static ILocator Option(IPage page, string name) =>
        page.Locator("[data-ui-option]").Filter(new() { HasText = name });

    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync(string page, bool slowReplies = false)
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<RangeFormApp>(
            playwright, path: "/uj/ranges/" + page, pathBase: "/uj", environment: "Development", endpointRouting: true,
            beforeLoad: async opened =>
            {
                frames = SocketFrames.Of(opened);
                if (slowReplies)
                {
                    await SlowRepliesAsync(opened);
                }
            });
        await Expect(session.Page.Locator("#ready")).ToHaveTextAsync("ready");

        return (session, frames!);
    }

    // Waits for the page to have HEARD that the list is open before anything is pressed in it.
    private static async Task OpenListAsync(IPage page)
    {
        await page.ClickAsync(Trigger);
        await Expect(page.Locator(Trigger)).ToHaveAttributeAsync("aria-expanded", "true");
    }

    private static Task<string[]> PostedAsync(IPage page, string name) =>
        page.EvaluateAsync<string[]>("name => new FormData(document.querySelector('form')).getAll(name).map(String)", name);

    [Theory]
    [InlineData("new")]
    [InlineData("new-named")]
    public async Task Every_step_of_filling_the_reported_form_is_answered_with_a_diff_of_a_few_hundred_bytes(string form)
    {
        var (session, frames) = await OpenAsync(form);
        await using var _ = session;
        var page = session.Page;
        frames.Clear();

        await page.GetByLabel("Megnevezés").FillAsync("Keleti");
        await page.GetByLabel("Méret").FillAsync("600");
        await OpenListAsync(page);
        await Option(page, "Glock").ClickAsync();
        await Expect(page.Locator(Trigger)).ToHaveTextAsync("Glock");
        await Option(page, "Beretta").ClickAsync();
        await Expect(page.Locator(Trigger)).ToHaveTextAsync("2 selected");
        await Option(page, "Beretta").ClickAsync();
        await Expect(page.Locator(Trigger)).ToHaveTextAsync("Glock");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Trigger)).ToHaveAttributeAsync("aria-expanded", "false");

        Assert.True(frames.FullPages.Count == 0, "A step was answered with the whole document:" + Environment.NewLine + frames.Trace());
        Assert.InRange(frames.LargestReply, 1, 1_500);
    }

    [Fact]
    public async Task A_named_listbox_in_the_form_posts_one_field_a_picked_pistol_in_the_order_of_the_list()
    {
        var (session, _) = await OpenAsync("new-named");
        await using var closing = session;
        var page = session.Page;
        var none = await PostedAsync(page, "pistols");

        await OpenListAsync(page);
        await Option(page, "Walther").ClickAsync();
        await Expect(page.Locator(Trigger)).ToHaveTextAsync("Walther");
        var one = await PostedAsync(page, "pistols");
        await Option(page, "Glock").ClickAsync();
        await Expect(page.Locator(Trigger)).ToHaveTextAsync("2 selected");
        var two = await PostedAsync(page, "pistols");
        await Option(page, "Walther").ClickAsync();
        await Expect(page.Locator(Trigger)).ToHaveTextAsync("Glock");
        var fewer = await PostedAsync(page, "pistols");

        Assert.Equal([string.Empty], none);
        Assert.Equal(["7f3c"], one);
        Assert.Equal(["7f1c", "7f3c"], two);
        Assert.Equal(["7f1c"], fewer);
    }

    [Fact]
    public async Task A_form_filled_without_a_pause_saves_everything_when_its_pick_is_answered_with_a_slow_whole_page()
    {
        var (session, frames) = await OpenAsync("new-whole", slowReplies: true);
        await using var _ = session;
        var page = session.Page;

        await FillWithoutAPauseAsync(page);
        await page.Locator("#save").FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("Keleti|7f1c|600|12||Nyitott");
        Assert.Single(frames.FullPages);
    }

    [Fact]
    public async Task Escape_pressed_straight_after_a_pick_leaves_the_list_closed_when_every_reply_is_150_ms_late()
    {
        var (session, frames) = await OpenAsync("new", slowReplies: true);
        await using var _ = session;
        var page = session.Page;

        await FillWithoutAPauseAsync(page);
        await page.ClickAsync("#save", new() { Timeout = 5_000 });

        await Expect(page.Locator("#saved")).ToHaveTextAsync("Keleti|7f1c|600|12||Nyitott");
        await Expect(page.Locator(Trigger)).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
    }

    // The acceptance case handed to the event-batching and deferred-binding work. On main roughly one run in
    // three ends with the list open again over the fields and the save button: Escape lands between the reply
    // to the opening and the reply to the pick, both of which say "open", and the toggles chase each other.
    // Eight fresh pages, because the window is a few milliseconds wide and no delay opens it reliably.
    [Theory(Skip = "Fails on main about one run in three: a reply rendered before Escape was heard opens the list again. Acceptance case for the event-batching and deferred-binding work, which removes this skip.")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task A_form_filled_without_a_pause_is_saved_with_everything_that_was_entered(int run)
    {
        var (session, frames) = await OpenAsync("new");
        await using var _ = session;
        var page = session.Page;

        await FillWithoutAPauseAsync(page);
        var open = await page.Locator(Trigger).GetAttributeAsync("aria-expanded");
        await page.ClickAsync("#save", new() { Timeout = 5_000 });

        await Expect(page.Locator("#saved")).ToHaveTextAsync("Keleti|7f1c|600|12||Nyitott");
        Assert.True(frames.FullPages.Count == 0, $"run {run}, list open {open}:" + Environment.NewLine + frames.Trace());
    }

    private static async Task FillWithoutAPauseAsync(IPage page)
    {
        await page.ClickAsync(Trigger);
        await Option(page, "Glock").ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.GetByLabel("Megnevezés").FillAsync("Keleti");
        await page.GetByLabel("Méret").FillAsync("600");
        await page.GetByLabel("Pályák").FillAsync("12");
        await page.GetByLabel("Típus").SelectOptionAsync("Nyitott");
    }

    // Every reply reaches the page 150 ms after the server sent it, in order: a server a network away.
    private static Task SlowRepliesAsync(IPage page) =>
        page.RouteWebSocketAsync(new Regex("/rask/ws"), socket =>
        {
            var server = socket.ConnectToServer();
            var last = Task.CompletedTask;
            socket.OnMessage(frame => server.Send(frame.Text!));
            server.OnMessage(frame => last = DeliverAsync(last, Task.Delay(150), socket, frame.Text!));
        });

    private static async Task DeliverAsync(Task before, Task due, IWebSocketRoute socket, string text)
    {
        await before;
        await due;
        socket.Send(text);
    }
}
