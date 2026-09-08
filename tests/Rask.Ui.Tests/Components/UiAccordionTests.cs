namespace Rask.Ui.Tests.Components;

/// <summary>
///     The accordion, and the section that refuses to render outside one.
/// </summary>
public partial class UiAccordionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_open_section_is_the_one_whose_key_matches()
    {
        var html = Accordion(open: "pay");

        // Both classes appear, and which section carries which is the whole state. Asserting only that
        // `collapse-open` is present would pass with it on the wrong section.
        var shipping = Section(html, "Shipping");
        var payment = Section(html, "Payment");

        Assert.Contains("collapse-close", shipping);
        Assert.Contains("collapse-open", payment);
    }

    [Fact]
    public void No_key_open_closes_every_section()
    {
        var html = Accordion(open: null);

        Assert.DoesNotContain("collapse-open", html);
        Assert.Equal(2, Occurrences(html, "collapse-close"));
    }

    [Fact]
    public void A_closed_section_writes_collapse_close_rather_than_nothing()
    {
        // Same reasoning as the dropdown: `collapse` also opens on :focus-within, so omitting
        // `collapse-open` is not the same as being closed, and tabbing into a section would open one
        // the accordion believes is shut.
        Assert.Contains("collapse-close", Accordion(open: "pay"));
    }

    [Fact]
    public void Each_heading_announces_its_own_state()
    {
        var html = Accordion(open: "pay");

        Assert.Contains("aria-expanded=\"true\"", html);
        Assert.Contains("aria-expanded=\"false\"", html);
    }

    [Fact]
    public void The_headings_are_buttons_so_a_keyboard_can_reach_them() =>
        Assert.Equal(2, Occurrences(Accordion(open: null), "<button"));

    [Theory]
    [InlineData(UiMarker.Arrow, "collapse-arrow")]
    [InlineData(UiMarker.Plus, "collapse-plus")]
    public void Every_marker_writes_its_own_class(UiMarker marker, string expected) =>
        Assert.Contains(expected, Marked(marker));

    [Fact]
    public void A_section_outside_an_accordion_says_which_two_components_are_involved()
    {
        // Context.Required would name Context.Provide<UiAccordionState> — machinery nobody types. The
        // message a reader needs names what they actually wrote.
        var error = Assert.Throws<InvalidOperationException>(() => Orphan().ToHtml());

        Assert.Contains("UiAccordionSection", error.Message);
        Assert.Contains("UiAccordion", error.Message);
        Assert.Contains("Shipping", error.Message);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    // The markup for one section, sliced out of the whole so an assertion cannot pass by finding the
    // class on its neighbour.
    private static string Section(string html, string title)
    {
        var at = html.IndexOf(title, StringComparison.Ordinal);
        Assert.True(at >= 0, $"no section titled {title}");
        var start = html.LastIndexOf("<div class=\"collapse", at, StringComparison.Ordinal);
        return html[start..at];
    }

    private string Accordion(string? open) =>
        UiAccordion.Open(open)[
            UiAccordionSection.Key("ship").Title("Shipping")[P["Ships in two days."]],
            UiAccordionSection.Key("pay").Title("Payment")[P["Card or transfer."]]
        ].ToHtml();

    private string Marked(UiMarker marker) =>
        UiAccordion.Open("ship")[
            UiAccordionSection.Key("ship").Title("Shipping").Marker(marker)[P["…"]]
        ].ToHtml();

    private global::Rask.Core.Component Orphan() =>
        UiAccordionSection.Key("ship").Title("Shipping")[P["…"]];
}
