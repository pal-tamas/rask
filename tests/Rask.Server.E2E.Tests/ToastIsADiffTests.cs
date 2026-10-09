using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A toast arriving, leaving and taking another's place, on the socket: each is a diff, wherever the layout
///     placed <c>Ui.Toast</c>.
/// </summary>
/// <remarks>
///     Reported from an app whose every save and delete says so in a toast: on a form of ONE field the refusal's
///     toast cost 33,648 bytes arriving and 31,296 leaving, each the whole document. <c>Ui.Toast</c> drew nothing
///     until there was a toast, so its popover came and went among the layout's children, and a child that
///     arrives before a sibling is a change by position. The host stays on the page now, closed and empty.
/// </remarks>
public sealed class ToastIsADiffTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Toast = "[data-ui-toast]";

    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync()
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<OneFieldToastPage>(playwright, beforeLoad: page =>
        {
            frames = SocketFrames.Of(page);
            return Task.CompletedTask;
        });
        await Expect(session.Page.Locator("#ready")).ToHaveTextAsync("ready");
        // The field is Live, so the name is with the page before Save: the frames counted after this are the toast's alone.
        await session.Page.GetByLabel("Name").FillAsync("taken");
        await session.Page.Keyboard.PressAsync("Tab");
        await Expect(session.Page.Locator("#heard")).ToHaveTextAsync("taken");
        frames!.Clear();

        return (session, frames);
    }

    [Fact]
    public async Task With_no_toast_the_host_is_on_the_page_closed_and_takes_no_room()
    {
        var (session, _) = await OpenAsync();
        await using var closing = session;
        var host = session.Page.Locator(Toast);

        var shape = await host.EvaluateAsync<string>(
            "e => [e.getAttribute('popover'), e.getAttribute('role'), e.matches(':popover-open'), getComputedStyle(e).display, e.childElementCount].join(' ')");

        Assert.Equal("manual status false none 0", shape);
    }

    [Fact]
    public async Task A_toast_arriving_and_leaving_are_each_answered_with_a_diff()
    {
        var (session, frames) = await OpenAsync();
        await using var closing = session;
        var page = session.Page;

        await page.ClickAsync("#save");
        await Expect(page.Locator(Toast)).ToContainTextAsync("That name is taken.");
        var shown = await page.Locator(Toast).EvaluateAsync<bool>("e => e.matches(':popover-open')");
        var arriving = frames.LargestReply;
        frames.Clear();
        await Expect(page.Locator(Toast + " [data-ui-toast-dialog]")).ToHaveCountAsync(0, new() { Timeout = 6_000 });
        await Expect(page.Locator("#heard")).ToHaveTextAsync("taken");

        Assert.True(shown);
        Assert.False(await page.Locator(Toast).EvaluateAsync<bool>("e => e.matches(':popover-open')"));
        Assert.True(frames.FullPages.Count == 0, "The toast leaving was answered with the whole document:" + Environment.NewLine + frames.Trace());
        Assert.InRange(arriving, 1, 4_000);
        Assert.InRange(frames.LargestReply, 1, 1_000);
    }

    [Fact]
    public async Task A_second_toast_takes_the_firsts_place_with_a_diff_and_plays_its_own_entrance()
    {
        var (session, frames) = await OpenAsync();
        await using var closing = session;
        var page = session.Page;
        await page.ClickAsync("#save");
        await Expect(page.Locator(Toast)).ToContainTextAsync("That name is taken.");
        await page.Locator(Toast + " [data-ui-toast-dialog]").EvaluateAsync("e => e.dataset.first = 'yes'");
        frames.Clear();

        await page.ClickAsync("#save");
        await Expect(page.Locator(Toast + " [data-ui-toast-dialog]:not([data-first])")).ToHaveCountAsync(1);

        // A new node, not the first one patched: its entrance animation and its countdown are its own.
        await Expect(page.Locator(Toast + " [data-ui-toast-dialog]")).ToHaveCountAsync(1);
        Assert.True(frames.FullPages.Count == 0, "The second toast was answered with the whole document:" + Environment.NewLine + frames.Trace());
    }
}

/// <summary>The reported form: one field in a guarded form, a save that only refuses, and the toasts placed before a footer.</summary>
public sealed partial class OneFieldToastPage : Component
{
    private readonly Draft _form = new();

    protected override Component? HeadAssets => Markup.Title["one field"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        Header[Nav[A.Href("/")["Home"]], H1["Destinations"]],
        Main[
            Form.Model(_form).OnSubmit(() => Toast.Error("That name is taken.").For(TimeSpan.FromSeconds(1))).ConfirmLeave("Leave without saving?")[
                Ui.Field[
                    Ui.Label.Badge("Required")["Name"],
                    Ui.Input.Bind(() => _form.Name).Live().MaxLength(40).ShowValidation(false).Validate(name => name.Length > 0 ? [] : ["Required"]),
                    Ui.Error
                ],
                Ui.Button.Primary.Submit.Id("save")["Save"]
            ],
            P.Id("heard")[_form.Name]
        ],
        Ui.Toast,
        Footer[P.Id("ready")["ready"]]
    ];

    private sealed class Draft
    {
        public string Name { get; set; } = string.Empty;
    }
}
