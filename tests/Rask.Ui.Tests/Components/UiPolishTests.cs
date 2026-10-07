namespace Rask.UiTests.Components;

/// <summary>
///     The smaller Flux UI affordances: a popover
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
