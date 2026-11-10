using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Querying;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A select whose search the page answers, in a list's filters over rows a query reads, opened without typing on a
///     server page: it offers what an empty search finds. TA's vehicle filter (2026-11-10) opened on "No results found"
///     — the filter was in force, so the page built its select before the rows had been read, and the select's one
///     question had been put to a search over no rows.
/// </summary>
public sealed class SearchedSelectOpensTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Panel = "dialog[data-ui-flyout]";

    [Theory]
    [InlineData("/searched-filters")]
    [InlineData("/searched-filters?vehicles=v2")]
    public async Task A_filter_over_rows_a_query_reads_opens_on_the_empty_searchs_answer(string path)
    {
        await using var session = await HookSession.OpenAsync<SearchedFiltersApp>(playwright, path: path, services: services =>
        {
            services.AddRaskQuery();
            services.AddSingleton<Rask.Cqrs.IDispatcher, FiltersJourneyPage.NothingToAsk>();
        });
        var page = session.Page;
        await Expect(page.Locator("#read")).ToHaveTextAsync("read");
        await page.ClickAsync("[data-ui-filters] [data-ui-modal-trigger] button");

        await page.Locator(Panel + " [data-ui-select-button]").ClickAsync();

        await Expect(page.Locator(Panel + " [data-ui-option]:not([data-hidden])")).ToHaveTextAsync(["ABC-123", "BCD-234", "CDE-345", "DEF-456"]);
    }
}

/// <summary>The app: its router.</summary>
public sealed partial class SearchedFiltersApp : Component
{
    protected override Component? HeadAssets => [Markup.Title["filters"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>
///     TA's transactions filters as they were: the vehicles a query reads, narrowed by a second query to those that
///     occur, offered while there are two or while one is chosen, searched by the page with the chosen rows as children.
/// </summary>
[Route("/searched-filters")]
public sealed partial class SearchedFiltersPage(IQueryClient queries) : Component
{
    private static readonly (string Id, string Name)[] Vehicles =
    [
        ("v1", "ABC-123"), ("v2", "BCD-234"), ("v3", "CDE-345"), ("v4", "DEF-456"),
    ];

    [QueryParam("vehicles")] public string? Chosen { get; set; }

    private Query<List<(string Id, string Name)>> VehicleChoices => field ??= queries.Query(
        QueryKey.Of("searched-filters-vehicles"), () => 1, async (_, ct) =>
        {
            await Task.Delay(200, ct);
            return Vehicles.ToList();
        });

    private Query<HashSet<string>> Occurring => field ??= queries.Query(
        QueryKey.Of("searched-filters-occurring"), () => 1, async (_, ct) =>
        {
            await Task.Delay(400, ct);
            return Vehicles.Select(v => v.Id).ToHashSet();
        });

    private List<(string Id, string Name)> Occurs(List<(string Id, string Name)>? choices) =>
        Occurring.Data is { } occurring ? [.. (choices ?? []).Where(choice => occurring.Contains(choice.Id))] : choices ?? [];

    protected override Component? Render()
    {
        List<string> chosen = Chosen is null ? [] : [Chosen];
        var choices = Occurs(VehicleChoices.Data);
        return Main[
            Ui.Filters[
                choices.Count > 1 || chosen.Count > 0
                    ? Ui.Filter.Key("filter-vehicles").Label("Vehicles").Shown(Chosen)[
                        Ui.Field[Ui.Label["Vehicles"], Many("vehicles", choices, chosen)]
                    ]
                    : null
            ],
            P.Id("read")[VehicleChoices.Data is not null && Occurring.Data is not null ? "read" : "reading"]
        ];
    }

    private static Component Many(string key, List<(string Id, string Name)> choices, List<string> chosen)
    {
        var sorted = choices.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        return Ui.Select.Key(key).Values((ICollection<string>)[.. chosen]).Listbox.Multiple().Placeholder("All")
            .OnChange((ICollection<string> _) => { })
            .Search(typed => Task.FromResult(sorted.Where(c => c.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .Select(c => (Component?)Ui.SelectOption.Key(c.Id).Value(c.Id)[c.Name])))[
                sorted.Where(c => chosen.Contains(c.Id)).Select(c => Ui.SelectOption.Key(c.Id).Value(c.Id)[c.Name])
            ];
    }
}
