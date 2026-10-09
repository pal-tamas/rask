using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A form with a multiple select among its fields, filled in by somebody who does not wait: a pick, and at
///     once a number in the next field.
/// </summary>
/// <remarks>
///     Reported from an app: every pick was answered with the whole document (85 KB where a diff is a few hundred
///     bytes), and the number typed straight after it was empty on the server at save. The unit suites hold the
///     ops; what only a browser holds is that the reply IS a diff on the socket and that what was typed arrives.
/// </remarks>
public sealed class SelectPickKeepsTypingTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Trigger = "[data-ui-select-button]";

    private static ILocator Option(IPage page, string name) =>
        page.Locator("[data-ui-option]").Filter(new() { HasText = name });

    // Waits for the page to have HEARD that the list is open before anything is pressed in it. Escape pressed
    // before the reply to the opening lands is another matter, and not this suite's: that reply says "open", the
    // runtime opens the list the reader has just closed, and the two toggles chase each other (reported).
    private static async Task OpenListAsync(IPage page)
    {
        await page.ClickAsync(Trigger);
        await Expect(page.Locator(Trigger)).ToHaveAttributeAsync("aria-expanded", "true");
    }

    private Task<(HookSession Session, SocketFrames Frames)> OpenAsync() => OpenAsync<PistolFormPage>();

    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync<TPage>()
        where TPage : Component
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<TPage>(playwright, beforeLoad: page =>
        {
            frames = SocketFrames.Of(page);
            return Task.CompletedTask;
        });
        await Expect(session.Page.Locator("#ready")).ToHaveTextAsync("ready");

        return (session, frames!);
    }

    [Fact]
    public async Task Every_pick_is_answered_with_a_diff_and_the_number_typed_straight_after_reaches_the_save()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await page.GetByLabel("Name").FillAsync("Range one");
        frames.Clear();

        await OpenListAsync(page);
        await Option(page, "Glock").ClickAsync();
        await Option(page, "Beretta").ClickAsync();
        await Option(page, "Beretta").ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.GetByLabel("Size").FillAsync("600");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("Range one|Glock|600");
        Assert.True(frames.FullPages.Count == 0, "A pick was answered with the whole document:" + Environment.NewLine + frames.Trace());
        Assert.InRange(frames.LargestReply, 1, 4_000);
    }

    [Fact]
    public async Task A_refused_save_is_answered_with_a_diff_and_its_toast_shows()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await page.GetByLabel("Name").FillAsync("taken");
        await OpenListAsync(page);
        await Option(page, "Glock").ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.GetByLabel("Size").FillAsync("600");
        frames.Clear();

        await page.ClickAsync("#save");

        await Expect(page.Locator("[data-ui-toast]")).ToContainTextAsync("That name is taken.");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("");
        Assert.True(frames.FullPages.Count == 0, "The refusal was answered with the whole document:" + Environment.NewLine + frames.Trace());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_second_refusal_while_the_first_toast_is_up_shows_for_its_own_five_seconds(bool toastLast)
    {
        var (session, _) = toastLast ? await OpenAsync<PistolFormPage>() : await OpenAsync<PistolFormToastBeforeFooterPage>();
        await using var closing = session;
        var page = session.Page;
        await page.GetByLabel("Name").FillAsync("taken");
        await OpenListAsync(page);
        await Option(page, "Glock").ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.GetByLabel("Size").FillAsync("600");
        await page.ClickAsync("#save");
        await Expect(page.Locator("[data-ui-toast]")).ToContainTextAsync("That name is taken.");

        await page.WaitForTimeoutAsync(3_500);
        await page.ClickAsync("#save");
        await page.Mouse.MoveAsync(5, 5);
        await page.WaitForTimeoutAsync(3_000);

        // 6.5 s after the first toast, 3 s after the second: the first one's time is up, the second's is not.
        await Expect(page.Locator("[data-ui-toast]")).ToContainTextAsync("That name is taken.", new() { Timeout = 500 });
    }
}

/// <summary>The same form in a layout that places its toasts before the footer, where a toast arriving moves a sibling.</summary>
public sealed partial class PistolFormToastBeforeFooterPage : PistolFormPage
{
    protected override bool ToastLast => false;
}

/// <summary>The reported form: a name, a multiple listbox, a number, each validated beside itself, and a save that can refuse.</summary>
public partial class PistolFormPage : Component
{
    protected virtual bool ToastLast => true;

    private static readonly string[] Pistols =
    [
        "Glock", "Beretta", "Walther", "Sig Sauer", "Colt", "Ruger", "Smith & Wesson", "Heckler & Koch", "CZ",
        "Springfield", "Taurus", "Kimber", "Steyr",
    ];

    private readonly Draft _form = new();
    private string _saved = string.Empty;

    protected override Component? HeadAssets => Markup.Title["pistols"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        Header[Nav[A.Href("/")["Home"]], H1["Ranges"]],
        Main[
            Form.Model(_form).OnSubmit(Save).ConfirmLeave("Leave without saving?")[
                Ui.Field[
                    Ui.Label["Name"],
                    Ui.Input.Bind(() => _form.Name).ShowValidation(false).Validate(name => name.Length > 0 ? [] : ["Required"]),
                    Ui.Error
                ],
                Ui.Field[
                    Ui.Label["Pistols"],
                    Ui.Select.Bind(() => _form.Picked).Listbox.Multiple().Placeholder("Choose…").ShowValidation(false)
                        .Validate(picked => picked.Count > 0 ? [] : ["Required"])[
                        Pistols.Select(pistol => Ui.SelectOption.Key(pistol).Value(pistol)[pistol])
                    ],
                    Ui.Error
                ],
                Ui.Field[
                    Ui.Label["Size"],
                    Ui.Input.Bind(() => _form.Size).Type(InputType.Number).ShowValidation(false).Validate(size => size is >= 1 ? [] : ["Required"]),
                    Ui.Error
                ],
                Ui.Button.Primary.Submit.Id("save")["Save"]
            ],
            P.Id("saved")[_saved]
        ],
        ToastLast ? null : Ui.Toast,
        Footer[P.Id("ready")["ready"]],
        ToastLast ? Ui.Toast : null
    ];

    private void Save()
    {
        if (string.Equals(_form.Name, "taken", StringComparison.Ordinal))
        {
            Toast.Error("That name is taken.");
            return;
        }

        _saved = $"{_form.Name}|{string.Join(',', _form.Picked)}|{_form.Size}";
    }

    private sealed class Draft
    {
        public string Name { get; set; } = string.Empty;

        public List<string> Picked { get; set; } = [];

        public int? Size { get; set; }
    }
}
