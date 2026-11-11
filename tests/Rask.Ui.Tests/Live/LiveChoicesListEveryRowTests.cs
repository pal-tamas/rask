using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     A live <c>Ui.Select.Bind(f).Choices(rows)</c> — <c>.Live()</c> and an <c>AfterBind</c> — lists every row it was
///     handed, as the same select without <c>.Live()</c> does (TA, nightly .3033: the live one opened on "Nincs
///     találat" with no row at all).
/// </summary>
public sealed partial class LiveChoicesListEveryRowTests : global::Rask.Core.RaskMarkup
{
    private static readonly Ui.Choice<int>[] Companies =
    [
        new(1, "Alfa Kft."), new(2, "Béta Zrt."), new(3, "Gamma Bt."), new(4, "Delta Kft."),
        new(5, "Epszilon Zrt."), new(6, "Zéta Bt."), new(7, "Éta Kft."),
    ];

    private sealed class Supply
    {
        public int? CompanyId { get; set; }

        public int AfterBinds { get; set; }

        public List<int> VehicleIds { get; set; } = [];

        // What the page has read so far: TA's editor reads its rows in OnMount, after the first render.
        public Ui.Choice<int>[] Rows { get; set; } = Companies;
    }

    private static int Options(string html) =>
        Regex.Count(html, "role=\"option\"", RegexOptions.None, TimeSpan.FromSeconds(1));

    // TA's Picker, step for step: the rows first, then the key, the live flag, the after-bind and the listbox's own.
    private static StubComponent Picker(Supply supply, bool live) => new(() =>
        Ui.Select.Bind(() => supply.CompanyId).Choices(supply.Rows)
            .Key("company").Live(live).AfterBind(_ => supply.AfterBinds++).Listbox.Searchable(true).Placeholder("Válasszon…")
            .Clearable(false).Disabled(false).ShowValidation(false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_select_of_choices_lists_every_row_whether_it_is_live_or_not(bool live)
    {
        var supply = new Supply();

        using var session = new OlderPage(Picker(supply, live));

        Assert.Equal(Companies.Length, Options(session.Loaded));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("hu-HU")]
    public async Task A_live_select_of_choices_picks_a_row_runs_its_after_bind_and_lists_every_row_again(string culture)
    {
        var supply = new Supply();
        var (before, beforeUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using var session = new OlderPage(Picker(supply, live: true));
            var rows = session.Ids("click");

            var heard = await session.PressedAsync(rows[^1]);

            Assert.True(heard);
            Assert.NotNull(supply.CompanyId);
            Assert.Equal(1, supply.AfterBinds);
            Assert.Equal(Companies.Length, Options(session.Latest));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (before, beforeUi);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rows_read_after_the_first_render_are_listed_whether_the_select_is_live_or_not(bool live)
    {
        var supply = new Supply { Rows = [] };
        using var session = new OlderPage(Picker(supply, live));
        supply.Rows = Companies;

        var drawn = session.Rerender();

        Assert.Equal(Companies.Length, Options(drawn));
    }

    [Fact]
    public void A_pillbox_keyed_after_its_choices_lists_the_rows_read_after_its_first_render()
    {
        var supply = new Supply { Rows = [] };
        using var session = new OlderPage(new StubComponent(() =>
            Ui.Pillbox.Bind(() => supply.VehicleIds).Choices(supply.Rows).Key("vehicles")));
        supply.Rows = Companies;

        var drawn = session.Rerender();

        Assert.Equal(Companies.Length, Options(drawn));
    }

    [Fact]
    public void A_select_keyed_after_its_choices_drops_the_rows_the_page_no_longer_offers()
    {
        var supply = new Supply();
        using var session = new OlderPage(Picker(supply, live: true));
        supply.Rows = Companies[..3];

        var drawn = session.Rerender();

        Assert.Equal(3, Options(drawn));
    }
}
