using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     An edit form in a real browser: a number the browser accepts and the bound member cannot hold must not be
///     "saved" as the value the row already had. The field says so, the save does not run, a correction saves.
/// </summary>
public sealed class EditFormUnreadValueTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData("weight", "99999999999")]
    [InlineData("share", "1e30")]
    public async Task A_number_too_large_for_the_stored_field_is_refused_and_the_old_value_is_not_saved_in_its_place(
        string field, string typed)
    {
        await using var session = await HookSession.OpenAsync<StoredTankPage>(playwright);
        var page = session.Page;

        await page.FillAsync("#" + field, typed);
        await page.ClickAsync("#save");
        await Expect(page.Locator("#" + field + "-error")).ToHaveTextAsync("The number is out of range.");
        await Expect(page.Locator("#" + field)).ToHaveValueAsync(typed);
        var savedWhileRefused = await page.Locator("#saved").TextContentAsync();
        await page.FillAsync("#" + field, "70");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync(field == "weight" ? "saved=70|27.5" : "saved=6500|70");
        await Expect(page.Locator("#" + field + "-error")).ToBeHiddenAsync();
        Assert.Equal("saved=", savedWhileRefused);
    }
}

/// <summary>An edit form over a stored tank: both fields already hold a value.</summary>
public sealed partial class StoredTankPage : Component
{
    private readonly Tank _tank = new();
    private string _saved = "";

    protected override Component? HeadAssets => Markup.Title["edit tank"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        Form.Model(_tank).OnSubmit(Save)[
            Ui.Input.Bind(() => _tank.Weight).Label("Weight").Id("weight"),
            Ui.Input.Bind(() => _tank.Share).Label("Share").Id("share"),
            Ui.Button.Submit.Id("save")["Save"]
        ]
    ];

    private void Save(Tank tank) => _saved = FormattableString.Invariant($"{tank.Weight}|{tank.Share}");

    private sealed class Tank
    {
        public int Weight { get; set; } = 6500;

        public decimal? Share { get; set; } = 27.5m;
    }
}
