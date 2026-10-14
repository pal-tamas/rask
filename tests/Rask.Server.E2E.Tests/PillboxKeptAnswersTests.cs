using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A pillbox whose options the page answers a search with keeps its pills when the answer no longer holds
///     their options — in a real browser over the real socket, as measured on Flux's "With backend search".
/// </summary>
public sealed class PillboxKeptAnswersTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static ILocator Pills(IPage page) => page.Locator("#tags [data-ui-pillbox-trigger] [data-value]");

    private static ILocator Rows(IPage page) => page.Locator("#tags [role=option]");

    [Fact]
    public async Task Three_pills_stay_through_a_search_that_excludes_them_and_one_taken_off_leaves_two_in_the_model()
    {
        await using var session = await HookSession.OpenAsync<PillboxKeptAnswersPage>(playwright);
        var page = session.Page;
        var input = page.Locator("#tags [data-ui-pillbox-input]");
        await input.ClickAsync();
        foreach (var name in new[] { "Marketing", "Design", "Development" })
        {
            await Rows(page).Filter(new() { HasText = name }).ClickAsync();
        }

        await Expect(page.Locator("#model")).ToHaveTextAsync("Marketing,Design,Development");
        // Back in the input, as a reader who goes on to type is: the presses on the rows left focus in the list,
        // and focus leaving it shuts it — the page hears of that before the first letter, not across it.
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#tags [popover]")).ToBeHiddenAsync();
        await input.ClickAsync();
        await Expect(page.Locator("#tags [popover]")).ToBeVisibleAsync();
        await input.PressSequentiallyAsync("sal");
        await Expect(Rows(page)).ToHaveTextAsync(["Sales"]);
        var throughTheSearch = await Pills(page).AllTextContentsAsync();
        // Dispatched, not pressed: the open list keeps the pointer from the trigger behind it.
        await Pills(page).Filter(new() { HasText = "Design" }).Locator("[data-rask-on-click]").DispatchEventAsync("click");

        Assert.Equal(["Marketing", "Design", "Development"], throughTheSearch.Select(text => text.Trim()));
        await Expect(page.Locator("#model")).ToHaveTextAsync("Marketing,Development");
        await Expect(Pills(page)).ToHaveTextAsync(["Marketing", "Development"]);
        await Expect(input).ToHaveValueAsync(string.Empty);
    }
}

/// <summary>A pillbox over a store the page searches.</summary>
public sealed partial class PillboxKeptAnswersPage : Component
{
    private static readonly string[] Tags = ["Design", "Development", "Marketing", "Sales", "Support"];

    private string[] _found = Tags;
    private ICollection<string> _picked = [];

    protected override Component? HeadAssets => [Markup.Title["pillbox kept answers"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Main[
        Div.Id("tags")[
            Ui.Pillbox.Values(_picked).OnChange(values => _picked = values).Combobox.Filter(false)[
                Ui.PillboxInput.OnInput(text => _found = [.. Tags.Where(tag => tag.Contains(text, StringComparison.OrdinalIgnoreCase))]).Placeholder("Choose tags..."),
                _found.Select(tag => Ui.PillboxOption.Key(tag).Value(tag)[tag])
            ]
        ],
        P.Id("model")[string.Join(",", _picked)]
    ];
}
