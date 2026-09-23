namespace Rask.UiTests.Components;

/// <summary>
///     The smaller Flux UI affordances: a resizable textarea, an outbound link, a shaped placeholder, a popover
///     that is not a menu.
/// </summary>
/// <remarks>
///     Each exists for the same reason: the alternative was a raw class string or hand-written markup at the
///     call site, and a class name written there is one the kit's own stylesheet never compiled — so it would
///     render as nothing at all while the build stayed green.
/// </remarks>
public partial class UiPolishTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_textarea_can_be_told_which_way_it_resizes()
    {
        Assert.Contains("resize-none",
            Ui.Textarea.Value("").Label("Notes").Resize(Ui.Resize.None).ToHtml(), StringComparison.Ordinal);
        Assert.Contains("resize-y",
            Ui.Textarea.Value("").Label("Notes").Resize(Ui.Resize.Vertical).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_sizing_is_CSS_rather_than_script()
    {
        // field-sizing: content is the platform's own answer, so it works on a prerendered page with no
        // runtime — and where an engine has not shipped it, the box keeps its rows and scrolls, which is
        // exactly what it does today.
        var html = Ui.Textarea.Value("").Label("Notes").AutoSize(true).ToHtml();

        Assert.Contains("ui-textarea-auto", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-input", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_link_opens_away_and_cannot_reach_back()
    {
        // All three together: a new tab without rel=noopener can reach the opener through window.opener, and
        // a tab that opens unannounced takes the back button away from a reader who did not ask for one.
        var html = Ui.Link.Text("The spec").Href("https://example.com").External(true).ToHtml();

        Assert.Contains("target=\"_blank\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
        Assert.Contains("opens in a new tab", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_link_is_untouched() =>
        Assert.DoesNotContain("target=\"_blank\"",
            Ui.Link.Text("Docs").Href("/docs").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_generated_route_is_never_external()
    {
        // It is one of your own pages by definition. Opening it in a second tab would be the app twice over.
        var route = new global::Rask.Core.Routing.RouteUrl("/docs", null, typeof(UiPolishTests));
        var html = Ui.Link.Text("Docs").Href(route).External(true).ToHtml();

        Assert.DoesNotContain("target=\"_blank\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_placeholder_of_lines_ends_short_the_way_a_paragraph_does()
    {
        // A stack of equal bars reads as a table. The eye notices that before the content lands.
        var html = Ui.Skeleton.Lines(3).ToHtml();

        Assert.Equal(3, Occurrences(html, "skeleton"));
        Assert.Contains("w-3/5", html, StringComparison.Ordinal);
    }

    [Fact]
    public void One_line_is_not_shortened() =>
        Assert.DoesNotContain("w-3/5", Ui.Skeleton.Lines(1).ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_placeholder_says_nothing_to_a_screen_reader()
    {
        // A row of empty boxes read aloud is worse than silence.
        Assert.Contains("aria-hidden=\"true\"", Ui.Skeleton.Lines(2).ToHtml(), StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", Ui.Skeleton.Circle(true).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_popover_is_a_dialog_rather_than_a_menu()
    {
        // The gap Ui.Dropdown left: a filter panel is not a list of commands, and saying menu would promise
        // one — along with the arrow keys that walk it.
        var html = Ui.Popover.Trigger("Filters")[Div["anything"]].ToHtml();

        Assert.Contains("aria-haspopup=\"dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"menu\"", html, StringComparison.Ordinal);
        Assert.Contains("popover=\"auto\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_popover_panel_is_named_by_its_trigger() =>
        Assert.Contains("aria-labelledby=\"uipop-",
            Ui.Popover.Trigger("Filters")[Div["x"]].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_controlled_popover_is_mirrored_by_the_runtime()
    {
        Assert.Contains("data-rask-popover-open=\"true\"",
            Ui.Popover.Trigger("Filters").Open(true)[Div["x"]].ToHtml(), StringComparison.Ordinal);

        // Uncontrolled: the browser owns it and the attribute is absent, so nothing fights the reader.
        Assert.DoesNotContain("data-rask-popover-open",
            Ui.Popover.Trigger("Filters")[Div["x"]].ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_can_be_made_denser() =>
        Assert.Contains("card-sm", Ui.Card.Size(Ui.Size.Sm)["x"].ToHtml(), StringComparison.Ordinal);

    private static int Occurrences(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
