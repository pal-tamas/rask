using System.Globalization;
using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A vehicle list filtered by a range of weights, as Eszaf's vehicles page has one.</summary>
[Route("/ui-filter-range-tests/vehicles")]
public sealed partial class RangedVehiclesPage(RouteState route) : Component
{
    [QueryParam("weightFrom")] public int? WeightFrom { get; set; }

    [QueryParam("weightTo")] public int? WeightTo { get; set; }

    [QueryParam("sort")] public string? Sort { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var filters = Ui.Filters[Ui.Filter.Range(() => WeightFrom, () => WeightTo).Label("Weight")];

        return Main[
            filters,
            filters.Badges,
            P.Id("active")[filters.Active.ToString(CultureInfo.InvariantCulture)],
            P.Id("at")[string.Join('&', route.Query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + q.Value))]
        ];
    }
}

/// <summary>A range of numbers with a fraction, a norm in litres, given no label of the page's.</summary>
[Route("/ui-filter-range-tests/norms")]
public sealed partial class RangedNormsPage : Component
{
    [QueryParam("normFrom"), System.ComponentModel.DataAnnotations.Display(Name = "Norm")] public decimal? NormFrom { get; set; }

    [QueryParam("normTo")] public decimal? NormTo { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var filters = Ui.Filters[Ui.Filter.Range(() => NormFrom, () => NormTo)];

        return Main[filters, filters.Badges];
    }
}

/// <summary>A range over members the address does not fill.</summary>
[Route("/ui-filter-range-tests/unkept")]
public sealed partial class UnkeptRangePage : Component
{
    public int? Least { get; set; }

    public int? Most { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Ui.Filters[Ui.Filter.Range(() => Least, () => Most).Label("Weight")];
}

/// <summary><c>Ui.Filter.Range(…)</c> (#1360): two ends kept in the address, said on one badge in the reader's words.</summary>
[Collection(FilteredOrdersCollection.Name)]
public partial class UiFilterRangeTests : global::Rask.Core.RaskMarkup
{
    private const string Vehicles = "/ui-filter-range-tests/vehicles";

    private static IReadOnlyList<string> Badges(Page page) =>
        [.. page.FindAll("[data-ui-filters-badges] [data-ui-badge]").Select(badge => badge.TextContent.Trim())];

    private static Task Open(Page page) => page.On("dialog > input[data-rask-modal-shown]").Change("true");

    // The page visited as a reader of the language reads it, its words and its numbers, and whatever is done on it after.
    private static async Task<Page> Visit(string language, string url, Func<Page, Task>? then = null)
    {
        var (ui, numbers) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
        try
        {
            var page = Page.Visit(url);
            await (then?.Invoke(page) ?? Task.CompletedTask);
            return page;
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, numbers);
        }
    }

    // "5 000" as the language groups it: Hungarian with a no-break space.
    private static string Grouped(string language, int number) => number.ToString("#,0", CultureInfo.GetCultureInfo(language));

    [Theory]
    [InlineData("en", "Weight: at least {0}")]
    [InlineData("hu", "Weight: legalább {0}")]
    public async Task A_least_alone_says_at_least_in_the_readers_grouping(string language, string says)
    {
        var page = await Visit(language, Vehicles + "?weightFrom=5000");

        var badges = Badges(page);

        Assert.Equal([string.Format(CultureInfo.InvariantCulture, says, Grouped(language, 5000))], badges);
    }

    [Theory]
    [InlineData("en", "Weight: at most {0}")]
    [InlineData("hu", "Weight: legfeljebb {0}")]
    public async Task A_most_alone_says_at_most(string language, string says)
    {
        var page = await Visit(language, Vehicles + "?weightTo=7000");

        var badges = Badges(page);

        Assert.Equal([string.Format(CultureInfo.InvariantCulture, says, Grouped(language, 7000))], badges);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task Both_ends_are_one_badge_from_the_least_to_the_most(string language)
    {
        var page = await Visit(language, Vehicles + "?weightFrom=5000&weightTo=7000");

        var badges = Badges(page);

        Assert.Equal(["Weight: " + Grouped(language, 5000) + " – " + Grouped(language, 7000)], badges);
        Assert.Equal("1", page.TextOf("#active"));
    }

    [Theory]
    [InlineData("en", "Norm: at least 20.5")]
    [InlineData("hu", "Norm: legalább 20,5")]
    public async Task A_number_with_a_fraction_keeps_it_and_a_range_given_no_label_is_called_by_its_first_ends_name(string language, string says)
    {
        var page = await Visit(language, "/ui-filter-range-tests/norms?normFrom=20.5");

        var badges = Badges(page);

        Assert.Equal([says], badges);
    }

    [Fact]
    public void A_range_with_neither_end_set_is_no_filter()
    {
        var page = Page.Visit(Vehicles + "?sort=name");

        var active = page.TextOf("#active");

        Assert.Equal("0", active);
        Assert.False(page.Exists("[data-ui-filters-badges]"));
    }

    [Fact]
    public async Task Taking_the_badge_off_removes_both_ends_and_keeps_the_rest_of_the_address()
    {
        var page = Page.Visit(Vehicles + "?weightFrom=5000&weightTo=7000&sort=name");

        await page.On("[aria-label=\"Remove filter Weight\"]").Click();

        Assert.Equal("sort=name", page.TextOf("#at"));
        Assert.Empty(Badges(page));
    }

    [Fact]
    public async Task Clear_filters_takes_both_ends_out()
    {
        var page = Page.Visit(Vehicles + "?weightFrom=5000&weightTo=7000&sort=name");
        await Open(page);

        await page.Click("Clear filters");

        Assert.Equal("sort=name", page.TextOf("#at"));
    }

    [Theory]
    [InlineData("en", "From", "To")]
    [InlineData("hu", "-tól", "-ig")]
    public async Task The_panel_draws_the_two_ends_side_by_side_under_the_label(string language, string from, string to)
    {
        var page = await Visit(language, Vehicles, Open);

        var fields = page.FindAll("dialog fieldset input");

        Assert.Equal("Weight", page.TextOf("dialog fieldset > legend"));
        Assert.Equal([from, to], fields.Select(field => field.Attribute("placeholder")));
        Assert.Equal([from, to], fields.Select(field => field.Attribute("aria-label")));
        Assert.Equal(["f-weightfrom", "f-weightto"], fields.Select(field => field.Id ?? "(none) " + field.Path()));
    }

    [Fact]
    public async Task Typing_an_end_moves_the_address_under_its_own_key()
    {
        var page = Page.Visit(Vehicles + "?sort=name");
        await Open(page);

        await page.Type("5000").Into("From");

        Assert.Equal("sort=name&weightFrom=5000", page.TextOf("#at"));
        Assert.Equal(["Weight: at least 5,000"], Badges(page));
    }

    [Fact]
    public void A_range_over_members_the_address_does_not_fill_names_the_declaration_to_write()
    {
        var failure = Assert.ThrowsAny<Exception>(() => Page.Visit("/ui-filter-range-tests/unkept"));

        var message = failure.ToString();

        Assert.Contains("[QueryParam(\"least\")] public int? Least { get; set; }", message, StringComparison.Ordinal);
    }
}
