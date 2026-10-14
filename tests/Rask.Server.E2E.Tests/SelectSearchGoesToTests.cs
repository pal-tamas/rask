using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A list's filter as an app writes it: a multiple listbox over what the address holds, whose pick goes to the
///     address that holds the new answers, with a search field the page answers from its own rows.
/// </summary>
/// <remarks>
///     A pick under a search that waits for a pause empties the field. It once did so by waiting on the browser
///     after the page's <c>OnChange</c> had gone elsewhere — and a handler that navigated sends no frame until it
///     ends, so the pick, and the navigation with it, waited for an answer that never came.
/// </remarks>
public sealed class SelectSearchGoesToTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static ILocator Option(IPage page, string select, string name) =>
        page.Locator($"#{select} [data-ui-option]").Filter(new() { HasText = name });

    [Theory]
    [InlineData("plain")]
    [InlineData("searched")]
    [InlineData("heard")]
    public async Task Rows_pressed_in_a_filter_that_goes_to_its_address_are_picked(string select)
    {
        await using var session = await HookSession.OpenAsync<SelectSearchGoesToApp>(playwright, path: "/filters");
        var page = session.Page;
        await page.ClickAsync($"#{select} [data-ui-select-button]");
        await Expect(page.Locator($"#{select} [data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "true");

        await Option(page, select, "Driver 05").ClickAsync();
        await Expect(page.Locator($"#{select}-chosen")).ToHaveTextAsync("Driver 05");
        await Option(page, select, "Driver 09").ClickAsync();

        await Expect(page.Locator($"#{select}-chosen")).ToHaveTextAsync("Driver 05,Driver 09");
        await Expect(Option(page, select, "Driver 09")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Contains("Driver%2009", page.Url, StringComparison.Ordinal);
    }
}

/// <summary>The app: its router, and nothing else.</summary>
public sealed partial class SelectSearchGoesToApp : Component
{
    protected override Component? HeadAssets => [Markup.Title["select goes to"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>Two filters over the address: one with no search field, one with a search the page answers.</summary>
[Route("/filters")]
public sealed partial class SelectSearchGoesToPage : Component
{
    private static readonly string[] Drivers =
        [.. Enumerable.Range(1, 30).Select(i => "Driver " + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))];

    private string _typed = string.Empty;

    [QueryParam("plain")] public string? Plain { get; set; }

    [QueryParam("searched")] public string? Searched { get; set; }

    [QueryParam("heard")] public string? Heard { get; set; }

    protected override Component? Render() => Main[
        Filter("plain", Plain, search: false),
        Filter("searched", Searched, search: true),
        Filter("heard", Heard, search: true, pause: false)
    ];

    private Component Filter(string key, string? held, bool search, bool pause = true)
    {
        List<string> chosen = [.. (held ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)];
        var found = Drivers.Where(name => !chosen.Contains(name) && name.Contains(_typed, StringComparison.OrdinalIgnoreCase)).Take(20);

        return Div.Key(key)[
            Div.Id(key)[
                Ui.Select.Key(key + "-select").Values((ICollection<string>)[.. chosen]).Listbox.Multiple().Searchable(true).Filter(!search).Placeholder("All")
                    .OnChange((ICollection<string> ids) => Go.To(At(key, string.Join(',', ids))))[
                    search ? Ui.SelectSearch.Value(_typed).Debounce(pause ? TimeSpan.FromMilliseconds(250) : null).OnInput(typed => { _typed = typed; }) : null,
                    (search ? [.. Drivers.Where(chosen.Contains), .. found] : Drivers).Select(name => Ui.SelectOption.Key(name).Value(name)[name])
                ]
            ],
            P.Id(key + "-chosen")[string.Join(',', chosen)]
        ];
    }

    private static Rask.Core.Routing.RouteUrl At(string key, string ids) => key switch
    {
        "plain" => Routes.SelectSearchGoesToPage(Plain: ids),
        "searched" => Routes.SelectSearchGoesToPage(Searched: ids),
        _ => Routes.SelectSearchGoesToPage(Heard: ids),
    };
}
