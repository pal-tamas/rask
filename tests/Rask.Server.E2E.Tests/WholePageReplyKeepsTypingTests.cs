using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A reply never discards what the reader typed after the event that caused it — also when the reply is the
///     whole page.
/// </summary>
/// <remarks>
///     <para>
///         A diff touches only what changed, so a field somebody is typing into is left alone. A whole page is
///         morphed over the document, and every bound field in it carries the value the server knew when it
///         rendered: the one from BEFORE the typing. A field that sends each keystroke was already guarded
///         while it has focus; a field that sends on <c>change</c> (a number, a date) was not, so the digits
///         typed into it were replaced by the old value — and since the field then held what it held when it
///         was entered, leaving it raised no <c>change</c> either. Nothing was sent; the save had no number.
///     </para>
///     <para>
///         The page answers one press slowly, with a child that changes element (which the diff gate refuses),
///         so the whole page lands while the reader is in the next field.
///     </para>
/// </remarks>
public sealed class WholePageReplyKeepsTypingTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync()
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<SlowWholePagePage>(playwright, beforeLoad: page =>
        {
            frames = SocketFrames.Of(page);
            return Task.CompletedTask;
        });
        await Expect(session.Page.Locator("#ready")).ToHaveTextAsync("ready");
        frames!.Clear();

        return (session, frames);
    }

    private static async Task WholePageLandedAsync(IPage page, SocketFrames frames)
    {
        await Expect(page.Locator("#shape")).ToHaveTextAsync("div");
        Assert.Single(frames.FullPages);
    }

    [Fact]
    public async Task A_number_typed_while_a_whole_page_reply_is_on_its_way_stays_in_its_field_and_reaches_the_save()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;

        await page.ClickAsync("#swap");
        await page.GetByLabel("Size").ClickAsync();
        await page.Keyboard.TypeAsync("600");
        await WholePageLandedAsync(page, frames);
        var shown = await page.GetByLabel("Size").InputValueAsync();
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("|600");
        Assert.Equal("600", shown);
    }

    [Fact]
    public async Task Words_typed_while_a_whole_page_reply_is_on_its_way_stay_in_their_field_with_focus_and_reach_the_save()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;

        await page.ClickAsync("#swap");
        await page.GetByLabel("Name").ClickAsync();
        await page.Keyboard.TypeAsync("Range one");
        await WholePageLandedAsync(page, frames);
        var focused = await page.GetByLabel("Name").EvaluateAsync<bool>("field => document.activeElement === field");
        await page.Keyboard.TypeAsync(" east");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("Range one east|");
        Assert.True(focused);
    }

    [Fact]
    public async Task A_value_the_server_changed_still_replaces_what_is_in_a_number_field_the_reader_is_in()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await page.GetByLabel("Size").ClickAsync();

        await page.ClickAsync("#fill");
        await WholePageLandedAsync(page, frames);

        // The server has news for this field, so the render wins: only a value it did not change is the reader's.
        await Expect(page.GetByLabel("Size")).ToHaveValueAsync("42");
    }
}

/// <summary>A form beside a press that is answered slowly, and with the whole page.</summary>
public sealed partial class SlowWholePagePage : Component
{
    private readonly Draft _form = new();
    private bool _swapped;
    private string _saved = string.Empty;

    protected override Component? HeadAssets => Markup.Title["whole page reply"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        // A span that becomes a div before a sibling: by position, so the gate answers with the whole page.
        Section[_swapped ? Div.Id("shape")["div"] : Span.Id("shape")["span"], P["after"]],
        Button.Id("swap").OnClick(SwapSlowly)["swap"],
        Button.Id("fill").OnClick(FillSlowly)["fill"],
        Form.Model(_form).OnSubmit(() => _saved = $"{_form.Name}|{_form.Size}")[
            Ui.Field[Ui.Label["Name"], Ui.Input.Bind(() => _form.Name)],
            Ui.Field[Ui.Label["Size"], Ui.Input.Bind(() => _form.Size).Type(InputType.Number)],
            Ui.Button.Submit.Id("save")["Save"]
        ],
        P.Id("saved")[_saved],
        P.Id("ready")["ready"]
    ];

    private async Task SwapSlowly()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        _swapped = true;
    }

    private async Task FillSlowly()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        _form.Size = 42;
        _swapped = true;
    }

    private sealed class Draft
    {
        public string Name { get; set; } = string.Empty;

        public int? Size { get; set; }
    }
}
