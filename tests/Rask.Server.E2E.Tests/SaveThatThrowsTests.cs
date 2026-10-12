using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A save that throws, in a real browser on a live server page that wrote nothing for it: the reader is told
///     in a toast, in their language, with what they typed still there and the button ready for another try.
/// </summary>
public sealed class SaveThatThrowsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Hungarian = "A mentés nem sikerült. Kérjük, próbálja újra.";

    [Fact]
    public async Task A_hungarian_reader_is_told_in_a_toast_keeps_what_they_typed_and_can_save_again()
    {
        SocketFrames? frames = null;
        await using var session = await HookSession.OpenAsync<ThrowingSavePage>(
            playwright,
            options: new() { ViewportSize = new() { Width = 1000, Height = 700 }, Locale = "hu-HU" },
            beforeLoad: page =>
            {
                frames = SocketFrames.Of(page);
                return Task.CompletedTask;
            },
            environment: "Production",
            cultures: static c =>
            {
                c.SupportedCultures.Add("en");
                c.SupportedCultures.Add("hu");
            });
        var page = session.Page;
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=0");
        await page.FillAsync("#name", "Glock 17");
        await page.FillAsync("#note", "kölcsönben");

        await page.ClickAsync("#save");

        await Expect(page.GetByText(Hungarian)).ToBeVisibleAsync();
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=1");
        await Expect(page.Locator("#name")).ToHaveValueAsync("Glock 17");
        await Expect(page.Locator("#note")).ToHaveValueAsync("kölcsönben");
        await Expect(page.Locator("#save")).ToBeEnabledAsync();
        await Expect(page.Locator("body")).Not.ToContainTextAsync("hunter2");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=2");
        Assert.True(frames!.FullPages.Count == 0, "The toast was answered with the whole document:" + Environment.NewLine + frames.Trace());
    }
}

/// <summary>A form as an app writes it, with the kit's toasts in the layout and no error handling of its own, whose save throws.</summary>
public sealed partial class ThrowingSavePage : Component
{
    private readonly Pistol _pistol = new();
    private int _saves;

    protected override Component? HeadAssets => Markup.Title["a save that throws"];

    protected override Component? Render() => Div[Main[
        Form.Model(_pistol).OnSubmit(Save)[
            Input.Bind(() => _pistol.Name).Id("name"),
            Input.Bind(() => _pistol.Note).Id("note"),
            Button.Type(ButtonType.Submit).Id("save")["Mentés"]
        ],
        P.Id("saves")[$"saves={_saves}"]
    ], Ui.Toast
    ];

    private async Task Save(Pistol pistol)
    {
        _saves++;
        await Task.Delay(1);
        throw new InvalidOperationException("Login failed for user 'sa' with password 'hunter2'.");
    }

    private sealed class Pistol
    {
        public string Name { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;
    }
}
