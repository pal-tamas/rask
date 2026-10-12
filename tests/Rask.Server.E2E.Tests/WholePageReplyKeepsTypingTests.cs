using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A reply never discards what the reader typed after the event that caused it — also when the reply is the
///     whole page, and also when it is a morph of the very parent the fields are in.
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
///         The page answers one press slowly, with a child that changes element, so the reply lands while the
///         reader is in the next field. Directly under the body that is the whole page. Inside the form it is a
///         morph of the form's children — the fields among them — which runs the same morph over them and so
///         must keep exactly what the whole page keeps. Every journey here is taken both ways.
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

    // "" is the child of the body, answered with the whole page; "-inside" is the child of the form.
    private static async Task ReplyLandedAsync(IPage page, SocketFrames frames, string where)
    {
        await Expect(page.Locator("#shape" + where)).ToHaveTextAsync("div");
        if (where.Length == 0)
        {
            Assert.Single(frames.FullPages);
        }
        else
        {
            Assert.True(frames.FullPages.Count == 0 && frames.Morphs.Count == 1, frames.Trace());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("-inside")]
    public async Task A_number_typed_while_a_whole_page_or_a_morph_of_its_form_is_on_its_way_stays_in_its_field_and_reaches_the_save(string where)
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;

        await page.ClickAsync("#swap" + where);
        await page.GetByLabel("Size").ClickAsync();
        await page.Keyboard.TypeAsync("600");
        await ReplyLandedAsync(page, frames, where);
        var shown = await page.GetByLabel("Size").InputValueAsync();
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("|600|");
        Assert.Equal("600", shown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-inside")]
    public async Task Two_numbers_filled_one_after_the_other_while_a_whole_page_or_a_morph_of_their_form_is_on_its_way_both_reach_the_save(string where)
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;

        // The reported journey: a press, and at once the next two fields, nothing waited for.
        await page.ClickAsync("#swap" + where);
        await page.GetByLabel("Size").FillAsync("600");
        await page.GetByLabel("Count").FillAsync("100");
        await ReplyLandedAsync(page, frames, where);
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("|600|100");
        await Expect(page.GetByLabel("Size")).ToHaveValueAsync("600");
        await Expect(page.GetByLabel("Count")).ToHaveValueAsync("100");
    }

    [Theory]
    [InlineData("")]
    [InlineData("-inside")]
    public async Task Words_typed_while_a_whole_page_or_a_morph_of_their_form_is_on_its_way_stay_in_their_field_with_focus_and_reach_the_save(string where)
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;

        await page.ClickAsync("#swap" + where);
        await page.GetByLabel("Name").ClickAsync();
        await page.Keyboard.TypeAsync("Range one");
        await ReplyLandedAsync(page, frames, where);
        var focused = await page.GetByLabel("Name").EvaluateAsync<bool>("field => document.activeElement === field");
        await page.Keyboard.TypeAsync(" east");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("Range one east||");
        Assert.True(focused);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-inside")]
    public async Task A_value_the_server_changed_still_replaces_what_is_in_a_number_field_the_reader_is_in(string where)
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await page.GetByLabel("Size").ClickAsync();

        await page.ClickAsync("#fill" + where);
        await ReplyLandedAsync(page, frames, where);

        // The server has news for this field, so the render wins: only a value it did not change is the reader's.
        await Expect(page.GetByLabel("Size")).ToHaveValueAsync("42");
    }
}

/// <summary>
///     A form beside two presses that are answered slowly: one with the whole page (a child of the body changes
///     element), one with a morph of the form (a child of the form does).
/// </summary>
public sealed partial class SlowWholePagePage : Component
{
    private readonly Draft _form = new();
    private bool _swapped;
    private bool _swappedInside;
    private string _saved = string.Empty;

    protected override Component? HeadAssets => Markup.Title["whole page reply"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        // A span that becomes a div before a sibling, as a child of the body: by position, and with no element
        // of the page around it to send instead, so the gate answers with the whole page.
        _swapped ? Div.Id("shape")["div"] : Span.Id("shape")["span"],
        P["after"],
        Button.Id("swap").OnClick(SwapSlowly)["swap"],
        Button.Id("fill").OnClick(FillSlowly)["fill"],
        Button.Id("swap-inside").OnClick(SwapInsideSlowly)["swap inside"],
        Button.Id("fill-inside").OnClick(FillInsideSlowly)["fill inside"],
        Form.Model(_form).OnSubmit(() => _saved = $"{_form.Name}|{_form.Size}|{_form.Count}")[
            // The same change as a child of the form: the form's children are morphed, the fields with them.
            _swappedInside ? Div.Id("shape-inside")["div"] : Span.Id("shape-inside")["span"],
            Ui.Field[Ui.Label["Name"], Ui.Input.Bind(() => _form.Name)],
            Ui.Field[Ui.Label["Size"], Ui.Input.Bind(() => _form.Size).Type(InputType.Number)],
            Ui.Field[Ui.Label["Count"], Ui.Input.Bind(() => _form.Count).Type(InputType.Number)],
            Ui.Button.Submit.Id("save")["Save"]
        ],
        P.Id("saved")[_saved],
        P.Id("ready")["ready"],
    ];

    private async Task SwapInsideSlowly()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        _swappedInside = true;
    }

    private async Task FillInsideSlowly()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        _form.Size = 42;
        _swappedInside = true;
    }

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

        public int? Count { get; set; }
    }
}
