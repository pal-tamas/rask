using Rask.Core;
using Rask.Core.Forms;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Input.Viewable()</c> on a live SERVER page: the button shows the password and hides it again in the
///     browser, and what it shows is kept through a render the page makes meanwhile.
/// </summary>
public sealed class UiInputViewTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task The_reveal_button_shows_the_password_keeps_it_shown_through_a_render_and_hides_it_again()
    {
        await using var session = await HookSession.OpenAsync<InputViewPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        var toggle = page.GetByLabel("Toggle password visibility");

        await toggle.ClickAsync();
        await Expect(page.Locator("#secret")).ToHaveAttributeAsync("type", "text");
        await Expect(toggle).ToHaveAttributeAsync("data-viewable-open", "");
        // The page draws itself again, and what it draws says type="password" and a button that says nothing.
        await page.ClickAsync("#again");
        await Expect(page.Locator("#again")).ToHaveTextAsync("drawn 1");
        var typeAfterRender = await page.GetAttributeAsync("#secret", "type");
        var markAfterRender = await toggle.GetAttributeAsync("data-viewable-open");
        await toggle.ClickAsync();

        Assert.Equal("text", typeAfterRender);
        Assert.Equal(string.Empty, markAfterRender);
        await Expect(page.Locator("#secret")).ToHaveAttributeAsync("type", "password");
        await Expect(toggle).Not.ToHaveAttributeAsync("data-viewable-open", "");
        Assert.Equal("hunter2", await page.InputValueAsync("#secret"));
    }
}

/// <summary>A password field with a reveal button, and a button that makes the page draw itself again.</summary>
public sealed partial class InputViewPage : Component
{
    private int _drawn;
    private string _secret = "hunter2";

    protected override Component? HeadAssets => [Markup.Title["input view"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        // Drawn differently each time, so a render does rewrite the field's own attributes.
        Ui.Input.Value(_secret).Id("secret").Type(InputType.Password).Viewable().Placeholder("drawn " + _drawn).OnChange(v => _secret = v),
        Button.Id("again").OnClick(() => _drawn++)[$"drawn {_drawn}"]
    ];
}
