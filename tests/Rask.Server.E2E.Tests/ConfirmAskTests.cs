using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>await Ui.Confirm.Ask("…")</c> on the Server host, at a desktop's width and a phone's: a handler asks after the
///     press, the layout's dialog opens with the question, and the handler goes on with the answer — true for Yes,
///     false for No, the close button and Escape — with focus moved in and handed back.
/// </summary>
/// <remarks>The WebAssembly host's half is <c>ConfirmWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.</remarks>
public sealed class ConfirmAskTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Dialog = "#ui-confirm";
    private const string Yes = Dialog + " [data-rask-confirm-dialog=go]";
    private const string Message = Dialog + " [data-rask-confirm-dialog=message]";

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task Yes_after_a_press_answers_true_and_focus_goes_back_to_the_button(int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;

        await page.ClickAsync("#ask");
        await Expect(page.Locator(Message)).ToHaveTextAsync("Save it anyway?");
        // The modal's focus placeholder holds focus for an instant and then leaves it on nothing (ui.css), so what
        // stays true however slow the machine is: the dialog is modal and the first Tab lands on one of its controls.
        await page.Keyboard.PressAsync("Tab");
        var focusInside = await page.Locator(Dialog).EvaluateAsync<bool>(
            "d => d.matches(':modal') && d.contains(document.activeElement) && document.activeElement.matches('button, input')");
        await page.ClickAsync(Yes);

        await Expect(page.Locator("#answer")).ToHaveTextAsync("yes");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        Assert.True(focusInside);
        Assert.Equal("ask", await session.FocusAsync());
    }

    [Theory]
    [InlineData("no", 1280, 800)]
    [InlineData("escape", 1280, 800)]
    [InlineData("x", 1280, 800)]
    [InlineData("no", 390, 844)]
    [InlineData("escape", 390, 844)]
    public async Task Every_way_of_closing_it_but_Yes_answers_false(string way, int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;

        await page.ClickAsync("#ask");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        await CloseAsync(page, way);

        await Expect(page.Locator("#answer")).ToHaveTextAsync("no");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        Assert.Equal("ask", await session.FocusAsync());
    }

    [Fact]
    public async Task A_question_with_words_of_its_own_and_a_primary_Yes_leaves_the_layouts_for_the_next()
    {
        await using var session = await OpenAsync(1280, 800);
        var page = session.Page;
        var heading = page.Locator(Dialog + " [data-rask-confirm-dialog=heading]");

        await page.ClickAsync("#ask-own");
        await Expect(heading).ToHaveTextAsync("Km");
        var own = await WordsAsync(page);
        var ownLook = await page.Locator(Yes).GetAttributeAsync("class");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#answer")).ToHaveTextAsync("no");
        await page.ClickAsync("#ask");
        await Expect(heading).ToHaveTextAsync("Confirm");
        var layouts = await WordsAsync(page);
        var layoutsLook = await page.Locator(Yes).GetAttributeAsync("class");
        await page.ClickAsync(Yes);

        await Expect(page.Locator("#answer")).ToHaveTextAsync("yes");
        Assert.Equal(("Nem", "Igen"), own);
        Assert.Equal(("No", "Yes"), layouts);
        Assert.Contains("bg-accent", ownLook, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-red-500", ownLook, StringComparison.Ordinal);
        Assert.Contains("bg-red-500", layoutsLook, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_questions_at_once_are_asked_one_after_the_other()
    {
        await using var session = await OpenAsync(1280, 800);
        var page = session.Page;

        await page.ClickAsync("#ask-twice");
        await Expect(page.Locator(Message)).ToHaveTextAsync("First?");
        await page.ClickAsync(Yes);
        await Expect(page.Locator(Message)).ToHaveTextAsync("Second?");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");

        await Expect(page.Locator("#answer")).ToHaveTextAsync("yes,no");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
    }

    private static async Task<(string No, string Yes)> WordsAsync(IPage page) =>
        ((await page.TextContentAsync(Dialog + " [data-rask-confirm-dialog=no]"))!,
            (await page.TextContentAsync(Dialog + " [data-rask-confirm-dialog=go] > span"))!);

    private static async Task CloseAsync(IPage page, string way)
    {
        switch (way)
        {
            case "no":
                await page.ClickAsync(Dialog + " [data-rask-confirm-dialog=no]");
                break;
            case "x":
                await page.ClickAsync(Dialog + " button[aria-label='Close modal']");
                break;
            default:
                await page.Keyboard.PressAsync("Escape");
                break;
        }
    }

    private async Task<HookSession> OpenAsync(int width, int height)
    {
        var session = await HookSession.OpenAsync<ConfirmAskPage>(
            playwright, new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = height } });
        await session.HooksLoadedAsync();
        return session;
    }
}

/// <summary>Buttons whose handlers ask from code, beside the kit's dialog placed as a layout would.</summary>
public sealed partial class ConfirmAskPage : Component
{
    private string _answer = "";

    protected override Component? HeadAssets => Markup.Title["confirm ask"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("answer")[_answer],
        Button.Id("ask").OnClick(Ask)["ask"],
        Button.Id("ask-own").OnClick(AskOwn)["ask own"],
        Button.Id("ask-twice").OnClick(AskTwice)["ask twice"],
        Ui.Confirm
    ];

    private async Task Ask() => _answer = Said(await Ui.Confirm.Ask("Save it anyway?"));

    private async Task AskOwn() =>
        _answer = Said(await Ui.Confirm.Heading("Km").No("Nem").Yes("Igen").Primary().Ask("Rögzíti a km-t?"));

    private async Task AskTwice()
    {
        var first = Ui.Confirm.Ask("First?");
        var second = Ui.Confirm.Ask("Second?");
        _answer = Said(await first) + "," + Said(await second);
    }

    private static string Said(bool yes) => yes ? "yes" : "no";
}
