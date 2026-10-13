using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     Every panel the kit anchors to a trigger tells the runtime where it wants to be
///     (<c>data-rask-place</c>), so the open panel is kept inside the viewport as Flux UI's is.
/// </summary>
/// <remarks>
///     The anchor rules in each panel's <c>style</c> place it with no script and can only turn it to the other
///     side. What Flux does beyond that — the other alignment, a slide back inside the edge, a height that fits
///     — is the runtime's (<c>rask-place.ts</c>), from this one attribute. Where a panel then LANDS is held to
///     Flux's live pages by the open-state parity scripts (<c>scripts/flux/placement.mjs</c>).
/// </remarks>
public partial class UiPanelPlaceTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] States = ["Alabama", "Alaska"];

    // The data-rask-place of the one element that carries `marker`.
    private static string Place(string html, string marker)
    {
        var panel = Regex.Match(html, "<[^>]*" + Regex.Escape(marker) + "[^>]*>").Value;
        return Regex.Match(panel, "data-rask-place=\"([^\"]*)\"").Groups[1].Value;
    }

    [Fact]
    public void A_menu_asks_for_the_place_under_its_triggers_start_five_pixels_off()
    {
        var html = Ui.Dropdown[Button["Options"], Ui.Menu[Ui.MenuItem["Edit"]]].ToHtml().AsText();

        var place = Place(html, "data-ui-menu=");

        Assert.Equal("bottom start 5 0", place);
    }

    [Theory]
    [InlineData(Ui.DropdownPosition.Top, Ui.DropdownAlign.End, "top end 2 -15")]
    [InlineData(Ui.DropdownPosition.Bottom, Ui.DropdownAlign.Center, "bottom center 2 -15")]
    [InlineData(Ui.DropdownPosition.Right, Ui.DropdownAlign.Start, "right start 2 -15")]
    [InlineData(Ui.DropdownPosition.Left, Ui.DropdownAlign.Center, "left center 2 -15")]
    public void A_popover_asks_for_the_side_the_edge_the_gap_and_the_offset_its_dropdown_was_given(
        Ui.DropdownPosition position, Ui.DropdownAlign align, string expected)
    {
        var dropdown = Ui.Dropdown.Position(position).Align(align).Gap(2).Offset(-15)[Button["Options"], Ui.Popover["Words"]];

        var place = Place(dropdown.ToHtml().AsText(), "data-ui-popover=");

        Assert.Equal(expected, place);
    }

    [Fact]
    public void A_menu_of_links_asks_for_its_dropdowns_place_too()
    {
        var html = Ui.Dropdown.Position(Ui.DropdownPosition.Top)[Button["Options"], Ui.Navmenu[Ui.NavmenuItem["Account"]]].ToHtml().AsText();

        var place = Place(html, "data-ui-navmenu=");

        Assert.Equal("top start 5 0", place);
    }

    [Fact]
    public void A_context_menu_asks_for_the_place_at_the_pointer_and_names_no_anchor()
    {
        var html = Ui.Context[Div["Invoice 42"], Ui.Menu[Ui.MenuItem["Open"]]].ToHtml().AsText();

        var menu = Regex.Match(html, "<[^>]*data-ui-menu=[^>]*>").Value;

        Assert.Contains("data-rask-place=\"bottom end 5 0\"", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("position-anchor", menu, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.ContextPosition.BottomStart, "bottom start 12 3")]
    [InlineData(Ui.ContextPosition.BottomCenter, "bottom center 12 3")]
    [InlineData(Ui.ContextPosition.TopEnd, "top end 4 -3")]
    [InlineData(Ui.ContextPosition.TopStart, "top start 4 3")]
    public void A_context_menus_offset_is_counted_from_the_edge_it_is_aligned_to_and_away_from_the_pointer(
        Ui.ContextPosition position, string expected)
    {
        var context = Ui.Context.Position(position).Gap(8).Offset((3, 4))[Div["Invoice 42"], Ui.Menu[Ui.MenuItem["Open"]]];

        var place = Place(context.ToHtml().AsText(), "data-ui-menu=");

        Assert.Equal(expected, place);
    }

    [Fact]
    public void A_list_of_options_asks_for_the_place_under_its_button()
    {
        var html = Ui.Select.Value("a").Listbox[Ui.SelectOption.Key("a")["A"]].ToHtml().AsText();

        var place = Place(html, "data-ui-options");

        Assert.Equal("bottom start 5 0", place);
    }

    [Fact]
    public void An_autocompletes_list_asks_for_the_place_under_its_field()
    {
        var html = Ui.Autocomplete.Value("")[States.Select(state => Ui.AutocompleteItem.Key(state)[state])].ToHtml().AsText();

        var place = Place(html, "data-ui-autocomplete-items");

        Assert.Equal("bottom start 5 0", place);
    }

    [Fact]
    public void A_calendar_and_a_list_of_times_ask_for_the_place_under_their_trigger()
    {
        var date = Ui.DatePicker.Value(new DateOnly(2026, 1, 15)).Locale("en-US").ToHtml().AsText();
        var time = Ui.TimePicker.Of<TimeOnly?>().Locale("en-US").ToHtml().AsText();

        var places = new[] { Place(date, "popover=\"auto\""), Place(time, "popover=\"auto\"") };

        Assert.Equal(["bottom start 5 0", "bottom start 5 0"], places);
    }

    [Fact]
    public void A_tooltip_asks_for_the_place_over_the_middle_of_its_trigger_and_carries_its_gap_and_offset()
    {
        var plain = Ui.Tooltip.Content("Settings")[Button["Open"]].ToHtml().AsText();
        var moved = Ui.Tooltip.Content("Settings").Position(Ui.TooltipPosition.Right).Align(Ui.TooltipAlign.End).Gap(12).Offset(8)[Button["Open"]]
            .ToHtml().AsText();

        var places = new[] { Place(plain, "data-ui-tooltip-content"), Place(moved, "data-ui-tooltip-content") };

        Assert.Equal(["top center 5 0", "right end 12 8"], places);
    }
}
